"""Root-only first deployment. Secrets and DB archive arrive over SSH, never user-data."""

import ipaddress
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path("/opt/geumeum/backend")
PRIVATE = Path("/home/ubuntu/geumeum-private")


def run(*args, data=None, env=None):
    reply = subprocess.run(args, input=data, capture_output=True, env=env)
    if reply.returncode:
        # Do not print SQL, process input or credential-bearing environment values
        raise RuntimeError(f"Deployment command failed: {args[0]}; code={reply.returncode}")
    return reply.stdout


def put(path, content, mode=0o600):
    target = Path(path)
    target.write_text(content, encoding="utf-8")
    target.chmod(mode)


def main():
    if os.geteuid() != 0:
        raise RuntimeError("Root required")
    if Path("/etc/geumeum/deployed").exists():
        raise RuntimeError("First-deployment tool refuses to overwrite an existing installation")
    values = json.loads((PRIVATE / "secrets.json").read_text())
    ip = str(ipaddress.IPv4Address(values["public_ip"]))
    password = values["database_password"]
    if not re.fullmatch(r"[A-Za-z0-9_-]{40,100}", password):
        raise RuntimeError("Invalid generated database credential format")
    existing = run(
        "runuser",
        "-u",
        "postgres",
        "--",
        "psql",
        "-Atc",
        "SELECT 1 FROM pg_database WHERE datname='geumeum'",
    )
    if existing.strip():
        raise RuntimeError("Database already exists; refusing to restore over it")
    run(
        "runuser",
        "-u",
        "postgres",
        "--",
        "psql",
        "-v",
        "ON_ERROR_STOP=1",
        data=(
            f"CREATE ROLE geumeum LOGIN PASSWORD '{password}';\n"
            "CREATE DATABASE geumeum OWNER geumeum;\n"
        ).encode(),
    )
    with (PRIVATE / "database.dump").open("rb") as archive:
        result = subprocess.run(
            [
                "runuser",
                "-u",
                "postgres",
                "--",
                "pg_restore",
                "--no-owner",
                "--no-acl",
                "--role=geumeum",
                "--exit-on-error",
                "--dbname=geumeum",
            ],
            stdin=archive,
            capture_output=True,
        )
    if result.returncode:
        raise RuntimeError("Database restore failed; inspect DB before retrying")
    run(
        "runuser",
        "-u",
        "postgres",
        "--",
        "psql",
        "-d",
        "geumeum",
        "-v",
        "ON_ERROR_STOP=1",
        data=(
            b"UPDATE rooms SET state='closed'; "
            b"UPDATE auth_sessions SET revoked_at=NOW() WHERE revoked_at IS NULL; "
            b"UPDATE discord_tickets SET expires_at=NOW();"
        ),
    )
    service = {
        "GEUMEUM_ENVIRONMENT": "production",
        "GEUMEUM_PUBLIC_ORIGIN": f"https://{ip}",
        "GEUMEUM_HOST": "127.0.0.1",
        "GEUMEUM_PORT": "8766",
        "GEUMEUM_DATABASE_URL": f"postgresql+psycopg://geumeum:{password}@127.0.0.1:5432/geumeum",
        "GEUMEUM_GAME_HOSTING": "listen",
        "GEUMEUM_GAME_BUILD_ID": values["build_id"],
        "GEUMEUM_GAME_PORT_START": "18000",
        "GEUMEUM_GAME_MAX_ROOMS": "100",
    }
    for value in [*service.values(), *values["discord"].values()]:
        if "\n" in str(value) or "\r" in str(value):
            raise RuntimeError("Unexpected environment newline")
    put("/etc/geumeum/service.env", "".join(f"{k}={v}\n" for k, v in service.items()))
    put("/etc/geumeum/discord.env", "".join(f"{k}={v}\n" for k, v in values["discord"].items()))
    run(
        str(ROOT / ".venv/bin/alembic"),
        "-c",
        str(ROOT / "alembic.ini"),
        "upgrade",
        "head",
        env={**os.environ, **service},
    )
    for name in ("geumeum-api.service", "geumeum-discord.service"):
        shutil.copyfile(ROOT / "deploy/aws" / name, Path("/etc/systemd/system") / name)
    Path("/etc/caddy/certs").mkdir(mode=0o750, exist_ok=True)
    run("chown", "root:caddy", "/etc/caddy/certs")
    hook = f"""#!/bin/sh
set -eu
source=/etc/letsencrypt/live/{ip}
install -o root -g caddy -m 640 "$source/fullchain.pem" /etc/caddy/certs/fullchain.pem
install -o root -g caddy -m 640 "$source/privkey.pem" /etc/caddy/certs/privkey.pem
systemctl reload caddy
"""
    put("/usr/local/sbin/geumeum-cert-renewed", hook, 0o700)
    run("/usr/local/sbin/geumeum-cert-renewed")
    put(
        "/etc/caddy/Caddyfile",
        f"""{{
    default_sni {ip}
}}
http://{ip} {{
    handle /.well-known/acme-challenge/* {{
        root * /var/www/acme
        file_server
    }}
    handle {{
        redir https://{ip}{{uri}} permanent
    }}
}}
https://{ip} {{
    tls /etc/caddy/certs/fullchain.pem /etc/caddy/certs/privkey.pem
    request_body {{
        max_size 1MB
    }}
    reverse_proxy 127.0.0.1:8766 {{
        header_up X-Forwarded-For {{remote_host}}
        header_up X-Forwarded-Proto https
    }}
    header Strict-Transport-Security "max-age=31536000"
}}
""",
        0o644,
    )
    run("caddy", "validate", "--config", "/etc/caddy/Caddyfile")
    put(
        "/etc/systemd/system/geumeum-cert-renew.service",
        """[Unit]
Description=Renew Geumeum IP TLS certificate
[Service]
Type=oneshot
ExecStart=/opt/geumeum-tools/bin/certbot renew --quiet \\
    --deploy-hook /usr/local/sbin/geumeum-cert-renewed
""",
        0o644,
    )
    put(
        "/etc/systemd/system/geumeum-cert-renew.timer",
        """[Unit]
Description=Check Geumeum TLS renewal every six hours
[Timer]
OnCalendar=*-*-* 00,06,12,18:00:00
RandomizedDelaySec=1800
Persistent=true
[Install]
WantedBy=timers.target
""",
        0o644,
    )
    run("systemctl", "daemon-reload")
    run("systemctl", "enable", "--now", "geumeum-api", "geumeum-cert-renew.timer")
    run("systemctl", "reload", "caddy")
    # Start the bot separately after HTTPS and restored account data are verified
    put("/etc/geumeum/deployed", f"https://{ip}\n")
    print("API deployment configured; Discord bot awaits cutover verification")


if __name__ == "__main__":
    main()
