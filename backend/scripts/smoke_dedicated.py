"""Verify three distinct local accounts against an actual dedicated Unity server."""

import asyncio
import json
import os
import secrets
import subprocess
from pathlib import Path

from sqlalchemy import delete, select

from geumeum_backend.accounts import create_account
from geumeum_backend.cli import windows_loop
from geumeum_backend.config import Settings
from geumeum_backend.db import database
from geumeum_backend.models import (
    Account,
    AuditEvent,
    AuthSession,
    JoinTicket,
    RefreshToken,
    Room,
    RoomMember,
)
from geumeum_backend.schemas import AccountCreate
from geumeum_backend.security import Passwords


async def main():
    await verify(3, "FullyQualifiedName~LiveDedicatedTests", "backend/.local/dedicated-check")


async def verify(count, selector, artifacts):
    """Seed throwaway accounts, run one live dotnet filter against them, then clean up."""
    settings = Settings()
    if settings.environment != "local":
        raise RuntimeError("Local environment required")
    root = Path(__file__).resolve().parents[2]
    engine, sessions = database(settings)
    ids, users = [], []
    try:
        for i in range(count):
            login, password = "ded_" + secrets.token_hex(6), secrets.token_urlsafe(9)
            async with sessions() as db:
                account = await create_account(
                    db,
                    Passwords(),
                    AccountCreate(login_id=login, password=password, nickname=f"Verify{i}"),
                )
                ids.append(account.id)
                users.append([login, password])
        environment = os.environ.copy()
        environment.update(
            GEUMEUM_SMOKE_DEDICATED=settings.game_executable,
            GEUMEUM_SMOKE_ORIGIN=f"http://127.0.0.1:{settings.port}",
            GEUMEUM_SMOKE_ARTIFACTS=str(root / artifacts),
            GEUMEUM_SMOKE_USERS=json.dumps(users),
        )
        result = await asyncio.to_thread(
            subprocess.run,
            [
                "dotnet",
                "test",
                str(root / "launcher/Account.Client.Tests/Account.Client.Tests.csproj"),
                "--filter",
                os.environ.get("GEUMEUM_SMOKE_FILTER", selector),
                "--nologo",
            ],
            env=environment,
            cwd=root,
            check=False,
        )
        if result.returncode:
            raise RuntimeError("Dedicated integration failed; inspect .local logs")
    finally:
        await asyncio.sleep(3)
        async with sessions() as db:
            owned = select(Room.id).where(Room.owner_id.in_(ids))
            await db.execute(delete(JoinTicket).where(JoinTicket.room_id.in_(owned)))
            await db.execute(delete(RoomMember).where(RoomMember.room_id.in_(owned)))
            await db.execute(delete(Room).where(Room.owner_id.in_(ids)))
            auth = select(AuthSession.id).where(AuthSession.account_id.in_(ids))
            await db.execute(delete(RefreshToken).where(RefreshToken.session_id.in_(auth)))
            await db.execute(delete(AuthSession).where(AuthSession.account_id.in_(ids)))
            await db.execute(delete(AuditEvent).where(AuditEvent.account_id.in_(ids)))
            await db.execute(delete(Account).where(Account.id.in_(ids)))
            await db.commit()
        await engine.dispose()


if __name__ == "__main__":
    windows_loop()
    asyncio.run(main())
