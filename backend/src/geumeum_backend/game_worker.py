"""Local allocator. Executable and ports are administrator configuration, never HTTP input."""

import asyncio
import json
import os
import subprocess
from datetime import timedelta
from pathlib import Path

from sqlalchemy import select, text, update

from .accounts import session_valid
from .cli import windows_loop
from .config import Settings
from .db import database
from .models import Account, AuthSession, Room, RoomMember
from .security import digest, now, token


async def run():
    settings = Settings()
    if settings.game_hosting != "dedicated":
        raise RuntimeError("Listen hosting does not use a game worker")
    executable = Path(settings.game_executable).resolve()
    if settings.environment != "local" or not executable.is_file():
        raise RuntimeError("Configure a local game executable first")
    identity = executable.parent / (executable.stem + "_Data/StreamingAssets/build-identity.json")
    if json.loads(identity.read_text(encoding="utf-8-sig"))["releaseId"] != settings.game_build_id:
        raise RuntimeError("Game build identity does not match configured build")
    engine, sessions = database(settings)
    # Keep a dedicated PostgreSQL advisory lock for the entire worker lifetime
    lock = await engine.connect()
    if not await lock.scalar(text("SELECT pg_try_advisory_lock(74621002)")):
        await lock.close()
        await engine.dispose()
        raise RuntimeError("Another local game worker is running")
    children = {}
    logs = Path(".local/game-servers").resolve()
    logs.mkdir(parents=True, exist_ok=True)
    try:
        async with sessions() as db:
            # After a worker crash, old servers shut down on their next heartbeat
            await db.execute(
                update(Room)
                .where(
                    Room.hosting == "dedicated", Room.state.in_(["starting", "ready", "running"])
                )
                .values(state="closed")
            )
            await db.commit()
        stop_file = Path(".local/worker.stop")
        while not stop_file.exists():
            async with sessions() as db:
                await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
                waiting = list(
                    await db.scalars(
                        select(Room).where(Room.hosting == "dedicated", Room.state == "waiting")
                    )
                )
                for room in waiting:
                    owner = await db.get(RoomMember, (room.id, room.owner_id))
                    account = await db.get(Account, room.owner_id)
                    session = await db.get(AuthSession, owner.session_id) if owner else None
                    if (
                        not owner
                        or owner.expires_at <= now()
                        or not account
                        or not session
                        or not await session_valid(db, account, session)
                    ):
                        room.state = "closed"
                await db.commit()
                for room_id, process in list(children.items()):
                    room = await db.get(Room, room_id)
                    if room is None:
                        if process.poll() is None:
                            process.terminate()
                            await asyncio.to_thread(process.wait, 5)
                        del children[room_id]
                        continue
                    limit = 70 if room.state == "starting" else 40
                    expired = (room.heartbeat_at or room.created_at) < now() - timedelta(
                        seconds=limit
                    )
                    if (
                        process.poll() is not None
                        or room.state == "closed"
                        or expired
                        or room.created_at < now() - timedelta(hours=3)
                        or (
                            room.state != "running"
                            and room.launch_deadline
                            and room.launch_deadline <= now()
                        )
                    ):
                        room.state = "closed"
                        if process.poll() is None:
                            process.terminate()
                            try:
                                await asyncio.to_thread(process.wait, 5)
                            except subprocess.TimeoutExpired:
                                process.kill()
                        del children[room_id]
                await db.commit()
            async with sessions() as db:
                await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
                room = await db.scalar(
                    select(Room)
                    .where(Room.hosting == "dedicated", Room.state == "queued")
                    .order_by(Room.created_at)
                    .with_for_update(skip_locked=True)
                    .limit(1)
                )
                if room:
                    occupied = set(
                        await db.scalars(
                            select(Room.port).where(
                                Room.state.in_(["starting", "ready", "running"])
                            )
                        )
                    )
                    available = next(
                        (
                            p
                            for p in range(
                                settings.game_port_start,
                                settings.game_port_start + settings.game_max_rooms,
                            )
                            if p not in occupied
                        ),
                        None,
                    )
                    if available is not None:
                        raw_key = token()
                        room.port, room.server_hash, room.state, room.heartbeat_at = (
                            available,
                            digest(raw_key),
                            "starting",
                            now(),
                        )
                        environment = {
                            k: v for k, v in os.environ.items() if not k.startswith("GEUMEUM_")
                        }
                        environment.update(
                            GEUMEUM_ROOM_ID=str(room.id),
                            GEUMEUM_SERVER_KEY=raw_key,
                            GEUMEUM_ACCOUNT_ORIGIN=f"http://127.0.0.1:{settings.port}",
                            GEUMEUM_GAME_PORT=str(available),
                        )
                        arguments = [
                            str(executable),
                            "-batchmode",
                            "-nographics",
                            "-account-server",
                            "-logFile",
                            str(logs / f"{room.id}.log"),
                        ]
                        # Commit credentials before the child can send a heartbeat
                        await db.commit()
                        try:
                            children[room.id] = subprocess.Popen(
                                arguments,
                                cwd=executable.parent,
                                env=environment,
                                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
                            )
                        except OSError:
                            room.state = "closed"
                            await db.commit()
                            print("Game process launch failed; room closed")
            await asyncio.sleep(1)
    finally:
        for process in children.values():
            if process.poll() is None:
                process.terminate()
                try:
                    await asyncio.to_thread(process.wait, 5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    await asyncio.to_thread(process.wait)
        async with sessions() as db:
            if children:
                await db.execute(update(Room).where(Room.id.in_(children)).values(state="closed"))
                await db.commit()
        await lock.execute(text("SELECT pg_advisory_unlock(74621002)"))
        await lock.close()
        await engine.dispose()


def main():
    windows_loop()
    try:
        asyncio.run(run())
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
