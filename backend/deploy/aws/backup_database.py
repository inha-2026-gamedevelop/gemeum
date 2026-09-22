"""Daily local PostgreSQL backup; only this service's dated dumps expire after seven days."""

import os
import subprocess
from datetime import UTC, datetime, timedelta
from pathlib import Path

destination = Path("/var/backups/geumeum")
destination.mkdir(mode=0o700, exist_ok=True)
stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
target = destination / f"geumeum-{stamp}.dump"
temporary = target.with_suffix(".partial")
with temporary.open("wb") as output:
    os.chmod(temporary, 0o600)
    subprocess.run(
        ["runuser", "-u", "postgres", "--", "pg_dump", "-Fc", "--no-owner", "--no-acl", "geumeum"],
        stdout=output,
        check=True,
    )
temporary.replace(target)
cutoff = datetime.now(UTC) - timedelta(days=7)
for old in destination.glob("geumeum-*.dump"):
    if old.is_file() and datetime.fromtimestamp(old.stat().st_mtime, UTC) < cutoff:
        old.unlink()
print(f"Database backup saved: {target.name}")
