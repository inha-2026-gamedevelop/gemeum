"""Package an explicit source allowlist; never include .env, .local or game builds."""

import hashlib
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / ".local/aws-deploy/backend.tar.gz"
ALLOW = (
    "pyproject.toml",
    "uv.lock",
    "alembic.ini",
    "src/geumeum_backend",
    "migrations",
    "scripts/run_discord_bot.py",
    "deploy/aws",
)


def main():
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with tarfile.open(TARGET, "w:gz") as archive:
        for relative in ALLOW:
            source = ROOT / relative
            paths = sorted(source.rglob("*")) if source.is_dir() else [source]
            for path in paths:
                if path.is_symlink():
                    raise RuntimeError("Deployment sources must not contain symlinks")
                if not path.is_file() or "__pycache__" in path.parts or path.suffix == ".pyc":
                    continue
                archive.add(path, arcname=path.relative_to(ROOT).as_posix(), recursive=False)
                count += 1
    print(f"Packaged {count} source files: {TARGET}")
    print("SHA256 " + hashlib.sha256(TARGET.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()
