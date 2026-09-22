"""Exercise the running local HTTP service using a temporary account, then remove it."""

import asyncio
import os
import secrets
import subprocess
import sys
from pathlib import Path

import httpx
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
    settings = Settings()
    if settings.environment != "local":
        raise RuntimeError("Only the local environment may run this smoke check")
    engine, sessions = database(settings)
    account_id = None
    try:
        password = secrets.token_urlsafe(9)
        login_id = "smoke_" + secrets.token_hex(6)
        async with sessions() as db:
            account = await create_account(
                db,
                Passwords(),
                AccountCreate(
                    login_id=login_id,
                    nickname="SmokeCheck",
                    password=password,
                ),
            )
            account_id = account.id
        if "--launcher" in sys.argv or "--unity" in sys.argv:
            root = Path(__file__).resolve().parents[2]
            environment = os.environ.copy()
            environment.update(
                GEUMEUM_SMOKE_ORIGIN=f"http://127.0.0.1:{settings.port}",
                GEUMEUM_SMOKE_LOGIN=login_id,
                GEUMEUM_SMOKE_PASSWORD=password,
                GEUMEUM_SMOKE_ARTIFACTS=str(root / "backend/.local/launcher-check"),
            )
            if "--install" in sys.argv:
                environment.update(
                    GEUMEUM_SMOKE_REPO=str(root / "backend/.local/account-patch/repo"),
                    GEUMEUM_SMOKE_INSTALL=str(root / "backend/.local/account-game-install"),
                )
            if "--unity" in sys.argv:
                environment["GEUMEUM_SMOKE_UNITY"] = str(
                    root / "Builds/AccountVerification/gemeum.exe"
                )
            result = await asyncio.to_thread(
                subprocess.run,
                [
                    "dotnet",
                    "test",
                    str(root / "launcher/Account.Client.Tests/Account.Client.Tests.csproj"),
                    "--nologo",
                    "--filter",
                    "FullyQualifiedName~LiveUnityTests"
                    if "--unity" in sys.argv
                    else "FullyQualifiedName~LiveAccountTests",
                ],
                env=environment,
                cwd=root,
                check=False,
            )
            if result.returncode:
                raise RuntimeError("Launcher integration check failed")
            print("Launcher integration flow PASS")
            return
        async with httpx.AsyncClient(base_url=f"http://127.0.0.1:{settings.port}") as client:
            assert (await client.get("/health/ready")).status_code == 200
            assert (await client.get("/docs")).status_code == 200
            response = await client.post(
                "/v1/auth/login",
                json={
                    "login_id": login_id,
                    "password": password,
                },
            )
            assert response.status_code == 200
            initial = response.json()
            headers = {"Authorization": "Bearer " + initial["access_token"]}
            assert (await client.get("/v1/me", headers=headers)).json()["account_id"] == str(
                account_id
            )
            assert (
                await client.patch(
                    "/v1/me/nickname",
                    headers=headers,
                    json={
                        "nickname": "LocalCheck",
                    },
                )
            ).status_code == 200
            rotated = await client.post(
                "/v1/auth/refresh",
                json={
                    "refresh_token": initial["refresh_token"],
                },
            )
            assert rotated.status_code == 200
            assert (
                await client.post(
                    "/v1/auth/refresh",
                    json={
                        "refresh_token": initial["refresh_token"],
                    },
                )
            ).status_code == 401
            assert (
                await client.get(
                    "/v1/me",
                    headers={
                        "Authorization": "Bearer " + rotated.json()["access_token"],
                    },
                )
            ).status_code == 401
            print("HTTP smoke PASS: ready/docs/login/profile/nickname/rotation/replay rejection")
    finally:
        if account_id is not None:
            async with sessions() as db:
                owned_rooms = select(Room.id).where(Room.owner_id == account_id)
                await db.execute(delete(JoinTicket).where(JoinTicket.room_id.in_(owned_rooms)))
                await db.execute(delete(RoomMember).where(RoomMember.room_id.in_(owned_rooms)))
                await db.execute(delete(Room).where(Room.owner_id == account_id))
                owned_sessions = select(AuthSession.id).where(AuthSession.account_id == account_id)
                await db.execute(
                    delete(RefreshToken).where(RefreshToken.session_id.in_(owned_sessions))
                )
                await db.execute(delete(AuthSession).where(AuthSession.account_id == account_id))
                await db.execute(delete(AuditEvent).where(AuditEvent.account_id == account_id))
                await db.execute(delete(Account).where(Account.id == account_id))
                await db.commit()
            print("Temporary smoke account removed; no password or token printed")
        await engine.dispose()


if __name__ == "__main__":
    windows_loop()
    asyncio.run(main())
