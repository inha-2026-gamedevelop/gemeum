"""Workspace-local PostgreSQL, no Windows service or administrator privileges."""

import argparse
import hashlib
import os
import secrets
import subprocess
import sys
import zipfile
from pathlib import Path
from urllib.parse import urlsplit

import httpx
import psycopg
from dotenv import dotenv_values
from psycopg import sql

ROOT = Path(__file__).resolve().parents[1]
LOCAL = ROOT / ".local"
PG = LOCAL / "pgsql" / "bin"
DATA = LOCAL / "pgdata"
ARCHIVE_URL = "https://get.enterprisedb.com/postgresql/postgresql-18.6-1-windows-x64-binaries.zip"
ARCHIVE_SHA256 = "fbe23da234ee31547bf8a36d29dfd81e82b849df2d2b78d2eecb43d360252f8c"


def run(name, *args, check=True, env=None):
    return subprocess.run(
        [str(PG / (name + ".exe")), *map(str, args)],
        check=check,
        env=env,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )


def install():
    if (PG / "pg_ctl.exe").exists():
        return
    LOCAL.mkdir(exist_ok=True)
    archive = LOCAL / "postgresql.zip"
    print("Downloading PostgreSQL 18.6 Windows binaries from EDB...", flush=True)
    with httpx.stream("GET", ARCHIVE_URL, follow_redirects=True, timeout=120) as response:
        response.raise_for_status()
        with archive.open("wb") as output:
            for chunk in response.iter_bytes(1024 * 1024):
                output.write(chunk)
    with archive.open("rb") as source:
        actual_hash = hashlib.file_digest(source, "sha256").hexdigest()
    if actual_hash != ARCHIVE_SHA256:
        raise RuntimeError("PostgreSQL archive hash mismatch; refusing to extract")
    with zipfile.ZipFile(archive) as zipped:
        # Only server runtime; do not install pgAdmin or mutate system directories
        for member in zipped.infolist():
            if not member.filename.startswith(("pgsql/bin/", "pgsql/lib/", "pgsql/share/")):
                continue
            target = (LOCAL / member.filename).resolve()
            if not target.is_relative_to(LOCAL.resolve()):
                raise RuntimeError("Archive path escapes local runtime directory")
            zipped.extract(member, LOCAL)
    print("PostgreSQL extracted into backend/.local", flush=True)


def start():
    install()
    env_file = ROOT / ".env"
    password_file = LOCAL / "pg-admin-password"
    if not env_file.exists():
        if DATA.exists():
            raise RuntimeError("Existing DB without .env; restore its configuration first")
        password = secrets.token_urlsafe(32)
        password_file.write_text(password, encoding="utf-8")
        env_file.write_text(
            "GEUMEUM_ENVIRONMENT=local\n"
            f"GEUMEUM_DATABASE_URL=postgresql+psycopg://geumeum:{password}"
            "@127.0.0.1:55432/geumeum\n"
            f"GEUMEUM_TEST_DATABASE_URL=postgresql+psycopg://geumeum:{password}"
            "@127.0.0.1:55432/geumeum_test\n"
            "GEUMEUM_HOST=127.0.0.1\nGEUMEUM_PORT=8766\n",
            encoding="utf-8",
        )
    config = dotenv_values(env_file)
    url = config["GEUMEUM_DATABASE_URL"].replace("postgresql+psycopg:", "postgresql:")
    parsed = urlsplit(url)
    if (
        config.get("GEUMEUM_ENVIRONMENT") != "local"
        or parsed.hostname != "127.0.0.1"
        or parsed.port != 55432
        or parsed.path != "/geumeum"
        or parsed.username != "geumeum"
        or not parsed.password
    ):
        raise RuntimeError("Portable DB tool only manages the local geumeum cluster on port 55432")
    if not (DATA / "PG_VERSION").exists():
        # May recover an interrupted initialization using the stored local configuration
        password_file.write_text(parsed.password, encoding="utf-8")
        try:
            run(
                "initdb",
                "-D",
                DATA,
                "-U",
                "geumeum",
                "--encoding=UTF8",
                "--locale=C",
                "--auth=scram-sha-256",
                f"--pwfile={password_file}",
            )
        finally:
            password_file.unlink(missing_ok=True)
    else:
        password_file.unlink(missing_ok=True)
    if run("pg_ctl", "-D", DATA, "status", check=False).returncode != 0:
        run(
            "pg_ctl",
            "-D",
            DATA,
            "-l",
            LOCAL / "postgres.log",
            "-o",
            "-h 127.0.0.1 -p 55432",
            "-w",
            "start",
        )
    admin_url = url.rsplit("/", 1)[0] + "/postgres"
    with psycopg.connect(admin_url, autocommit=True) as conn:
        for name in ("geumeum", "geumeum_test"):
            if not conn.execute("SELECT 1 FROM pg_database WHERE datname = %s", (name,)).fetchone():
                conn.execute(
                    sql.SQL("CREATE DATABASE {} ENCODING 'UTF8'").format(sql.Identifier(name))
                )
    print("Local PostgreSQL ready on 127.0.0.1:55432; credentials in ignored .env")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["start", "stop", "status"])
    args = parser.parse_args()
    if os.name != "nt":
        raise SystemExit("Use a local PostgreSQL 18 installation on non-Windows hosts")
    if args.action == "start":
        start()
    elif args.action == "stop":
        run("pg_ctl", "-D", DATA, "-m", "fast", "-w", "stop")
    else:
        sys.exit(run("pg_ctl", "-D", DATA, "status", check=False).returncode)


if __name__ == "__main__":
    main()
