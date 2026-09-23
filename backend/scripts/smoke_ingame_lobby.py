"""Run the launcher-authenticated, three-client in-game lobby smoke test locally."""

import asyncio
import os
from pathlib import Path
from urllib.parse import urlsplit

from smoke_dedicated import verify

from geumeum_backend.cli import windows_loop
from geumeum_backend.config import Settings


async def main():
    settings = Settings()
    root = Path(__file__).resolve().parents[2]
    configured_executable = Path(settings.game_executable)
    executable = (configured_executable if configured_executable.is_absolute()
                  else root / configured_executable).resolve()
    database = urlsplit(settings.database_url.get_secret_value())
    if (
        settings.environment != "local"
        or settings.port != 18766
        or settings.game_hosting != "listen"
        or not settings.relay_enabled
        or settings.relay_bind_address != "127.0.0.1"
        or settings.relay_public_address != "127.0.0.1"
        or settings.relay_port != 18767
        or database.hostname not in {"127.0.0.1", "localhost", "::1"}
        or not executable.is_relative_to(root / "Builds")
        or not executable.is_file()
        or not settings.game_build_id
        or "GEUMEUM_SMOKE_FILTER" in os.environ
    ):
        raise RuntimeError("Local in-game lobby smoke configuration is incomplete or unsafe")
    os.environ["GEUMEUM_GAME_EXECUTABLE"] = str(executable)
    await verify(3, "FullyQualifiedName~LiveInGameLobbyTests", "backend/.local/ingame-lobby-check")


if __name__ == "__main__":
    windows_loop()
    asyncio.run(main())
