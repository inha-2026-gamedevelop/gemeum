"""Verify that a departed party member returns to a live listen host, using real clients."""

import asyncio

from smoke_dedicated import verify

from geumeum_backend.cli import windows_loop

# Four accounts: the host, the member who leaves and returns, one who stays, one outsider
ACCOUNTS = 4

if __name__ == "__main__":
    windows_loop()
    asyncio.run(
        verify(ACCOUNTS, "FullyQualifiedName~LiveReconnectTests", "backend/.local/reconnect-check")
    )
