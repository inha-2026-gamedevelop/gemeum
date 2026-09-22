#!/bin/bash
# Ubuntu 24.04 foundation only, no credentials are stored in EC2 user data
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq curl ca-certificates python3-venv caddy
install -d /usr/share/postgresql-common/pgdg
curl -fsS https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
cat >/etc/apt/sources.list.d/pgdg.sources <<'EOF'
Types: deb
URIs: https://apt.postgresql.org/pub/repos/apt
Suites: noble-pgdg
Architectures: amd64
Components: main
Signed-By: /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
EOF
apt-get update -qq
apt-get install -y -qq postgresql-18 postgresql-client-18
id geumeum >/dev/null 2>&1 || useradd --system --create-home --home-dir /opt/geumeum --shell /usr/sbin/nologin geumeum
install -d -o geumeum -g geumeum /opt/geumeum/backend
install -d -m 700 /etc/geumeum
python3 -m venv /opt/geumeum-tools
/opt/geumeum-tools/bin/pip install --disable-pip-version-check uv 'certbot>=5.4,<6'
install -d -m 755 /var/www/acme
cat >/etc/caddy/Caddyfile <<'EOF'
:80 {
    root * /var/www/acme
    file_server
}
EOF
systemctl enable --now caddy postgresql
systemctl reload caddy
touch /var/lib/geumeum-bootstrap-ready
