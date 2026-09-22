import argparse
import asyncio
import getpass
import json
import sys
from pathlib import Path

import uvicorn
from fastapi import HTTPException
from pydantic import ValidationError
from sqlalchemy import select

from .accounts import create_account, lock_account, revoke_all
from .config import Settings
from .db import database
from .models import Account
from .schemas import AccountCreate
from .security import Passwords


def windows_loop():
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())


def serve():
    settings = Settings()
    windows_loop()
    uvicorn.run(
        "geumeum_backend.api:create_app",
        factory=True,
        host=settings.host,
        port=settings.port,
        proxy_headers=settings.environment == "production",
        forwarded_allow_ips="127.0.0.1",
        loop="asyncio:SelectorEventLoop",
        access_log=False,
        limit_concurrency=32,
    )


async def administer(args):
    settings = Settings()
    if settings.environment != "local" and args.action not in {
        "record-release",
        "preview-notifications",
        "notification-status",
    }:
        raise RuntimeError("Local account administration is disabled outside local mode")
    engine, sessions = database(settings)
    try:
        if args.action == "record-release":
            from .releases import NewNotice, record_notice

            notice = NewNotice(
                product=args.product,
                channel=args.channel,
                version=args.version,
                notes=Path(args.notes_file).read_text(encoding="utf-8-sig").strip(),
            )
            async with sessions() as db:
                created = await record_notice(db, notice)
                await db.commit()
                print(
                    "Notice queued; the running Discord worker delivers configured channels"
                    if created
                    else "Notice already queued"
                )
        elif args.action == "preview-notifications":
            from .releases import preview

            async with sessions() as db:
                print(json.dumps(await preview(db), ensure_ascii=False, indent=2))
        elif args.action == "notification-status":
            from .models import NotificationOutbox, ReleaseNotice

            async with sessions() as db:
                rows = await db.execute(
                    select(NotificationOutbox, ReleaseNotice)
                    .join(ReleaseNotice)
                    .order_by(NotificationOutbox.created_at.desc())
                    .limit(50)
                )
                for item, notice in rows:
                    print(
                        f"{item.id} {notice.product}/{notice.channel}/{notice.version} "
                        f"state={item.state} attempts={item.attempts} "
                        f"channel={item.discord_channel_id} message={item.discord_message_id}"
                    )
        elif args.action == "create-user":
            password = getpass.getpass("Password (8-15 characters): ")
            if password != getpass.getpass("Confirm password: "):
                raise ValueError("Passwords do not match")
            data = AccountCreate(login_id=args.login_id, nickname=args.nickname, password=password)
            async with sessions() as db:
                account = await create_account(db, Passwords(), data)
                print(f"Local account created: {account.login_id} (id={account.id})")
        elif args.action == "set-status":
            async with sessions() as db:
                account = await db.scalar(
                    select(Account).where(
                        Account.login_id == args.login_id.strip().lower(),
                    )
                )
                if account is None:
                    raise ValueError("Account does not exist")
                account = await lock_account(db, account.id)
                account.status = args.status
                await revoke_all(db, account, "local_status_changed")
                await db.commit()
                print("Account status updated; all sessions revoked")
    finally:
        await engine.dispose()


def main():
    parser = argparse.ArgumentParser(description="Local account administration (no public signup)")
    sub = parser.add_subparsers(dest="action", required=True)
    create = sub.add_parser("create-user")
    create.add_argument("login_id")
    create.add_argument("nickname")
    status = sub.add_parser("set-status")
    status.add_argument("login_id")
    status.add_argument("status", choices=["active", "disabled"])
    release = sub.add_parser(
        "record-release", help="Queue a local notice after verifying the release"
    )
    release.add_argument("product", choices=["game", "launcher"])
    release.add_argument("channel", choices=["internal", "stable"])
    release.add_argument("version")
    release.add_argument("notes_file")
    sub.add_parser(
        "preview-notifications", help="Preview pending notices without sending or consuming"
    )
    sub.add_parser("notification-status", help="Show delivery states and Discord message IDs")
    args = parser.parse_args()
    windows_loop()
    try:
        asyncio.run(administer(args))
    except ValidationError as exc:
        # Do not print Pydantic's input values: they can include passwords
        print("Invalid account fields:", ", ".join(str(e["loc"]) for e in exc.errors()))
        raise SystemExit(2) from None
    except HTTPException as exc:
        print(exc.detail)
        raise SystemExit(2) from None
    except ValueError as exc:
        print(str(exc))
        raise SystemExit(2) from None
