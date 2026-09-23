"""Party directory and connection-bound tickets for account-authenticated hosts."""

import asyncio
import ipaddress
import logging
import secrets
import uuid
from datetime import timedelta
from typing import Annotated, Literal

from fastapi import Header, HTTPException, Request
from pydantic import Field, SecretStr
from sqlalchemy import delete, func, select, text
from sqlalchemy.exc import SQLAlchemyError

from .accounts import lock_account, session_valid
from .api import DB, Auth, recheck_locked, throttle
from .models import Account, AuthSession, JoinTicket, Room, RoomMember
from .schemas import Input, validate_nickname
from .security import digest, now, token


class NewRoom(Input):
    name: str = Field(min_length=2, max_length=32)
    build_id: str = Field(min_length=1, max_length=96)


class TicketRequest(Input):
    challenge: str = Field(pattern=r"^[a-f0-9]{64}$")
    build_id: str = Field(min_length=1, max_length=96)


class Redeem(TicketRequest):
    ticket: SecretStr = Field(min_length=40, max_length=128)


class SessionClaim(Input):
    account_id: str = Field(pattern=r"^[0-9]{1,19}$")
    session_id: uuid.UUID


class LobbyAction(Input):
    action: Literal["join", "leave", "pulse", "preference", "ready", "bots", "start"]
    preference: int | None = Field(default=None, ge=0, le=2)
    enabled: bool = False


class Heartbeat(Input):
    state: Literal["ready", "running"]
    accounts: list[str] = Field(max_length=3)
    sessions: list[SessionClaim] = Field(default_factory=list, max_length=3)


async def room_parent_session_id(db, auth):
    """Keep game-driven lobby seats bound to their valid launcher parent."""
    account, session = auth
    if session.kind == "launcher":
        return session.id
    if session.kind != "game" or session.parent_id is None:
        raise HTTPException(403, "Launcher-backed game session required")
    parent = await db.get(
        AuthSession, session.parent_id, with_for_update=True, populate_existing=True
    )
    if (
        parent is None
        or parent.kind != "launcher"
        or parent.account_id != account.id
        or not await session_valid(db, account, parent)
    ):
        raise HTTPException(403, "Launcher-backed game session required")
    return parent.id


def room_view(room, owner_name="", members=None):
    return dict(
        room_id=str(room.id),
        name=room.name,
        build_id=room.build_id,
        state=room.state,
        address=room.address or "127.0.0.1",
        hosting=room.hosting,
        port=room.port or 0,
        owner_id=str(room.owner_id),
        owner_nickname=owner_name,
        members=members or [],
        max_players=3,
        fill_with_bots=room.fill_with_bots,
    )


async def live_members(db, room):
    people = list(await db.scalars(select(RoomMember).where(RoomMember.room_id == room.id)))
    valid = []
    for member in people:
        account = await db.get(Account, member.account_id)
        if not account or member.expires_at <= now():
            continue
        # The seat belongs to the person, not to one game process. A member whose game
        # session ended keeps their place while the launcher session that joined is still
        # valid, which is what lets the same account find the room again and return
        for session_id in (member.session_id, member.launcher_session_id):
            session = await db.get(AuthSession, session_id) if session_id else None
            if (
                session
                and session.account_id == member.account_id
                and await session_valid(db, account, session)
            ):
                valid.append((member, account))
                break
    return valid


async def view(db, room):
    owner = await db.get(Account, room.owner_id)
    return room_view(
        room,
        owner.nickname if owner else "",
        [
            dict(
                account_id=str(account.id),
                nickname=account.nickname,
                is_owner=account.id == room.owner_id,
                connected=member.connected,
                preference=member.preference,
                ready=member.ready,
            )
            for member, account in await live_members(db, room)
        ],
    )


async def leave_other_rooms(db, account_id, target=None):
    existing = list(await db.scalars(select(RoomMember).where(RoomMember.account_id == account_id)))
    for member in existing:
        if member.room_id == target:
            continue
        room = await db.get(Room, member.room_id, with_for_update=True)
        if (
            room
            and room.state != "closed"
            and member.expires_at > now()
            and room.state != "waiting"
        ):
            raise HTTPException(409, "Cannot change rooms while a run is starting or active")
        if room and room.state == "waiting" and room.owner_id == account_id:
            room.state = "closed"
        await db.delete(member)
    await db.flush()


async def server_room(db, room_id, key):
    room = await db.get(Room, room_id)
    if (
        room is None
        or not key
        or not room.server_hash
        or not secrets.compare_digest(digest(key), room.server_hash)
    ):
        raise HTTPException(401, "Server credential required")
    if room.hosting == "listen":
        session = await db.get(AuthSession, room.host_session_id) if room.host_session_id else None
        account = await db.get(Account, room.owner_id)
        if not session or not account or not await session_valid(db, account, session):
            raise HTTPException(401, "Host session expired")
    return room


async def expire_listen_rooms(app):
    """API-owned leases; retry DB outages without losing the lease sweeper."""
    while True:
        try:
            await sweep_listen_rooms(app)
        except SQLAlchemyError:
            logging.getLogger("geumeum.rooms").warning("Room lease sweep failed; retrying")
        await asyncio.sleep(2)


async def sweep_listen_rooms(app):
    async with app.state.sessions() as db:
        await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
        items = list(
            await db.scalars(
                select(Room)
                .where(Room.hosting == "listen", Room.state != "closed")
                .with_for_update()
            )
        )
        for room in items:
            owner = await db.get(RoomMember, (room.id, room.owner_id))
            session_id = room.host_session_id or (owner.launcher_session_id if owner else None)
            session = await db.get(AuthSession, session_id) if session_id else None
            account = await db.get(Account, room.owner_id)
            expired = not session or not account or not await session_valid(db, account, session)
            if room.state == "waiting":
                expired |= not owner or owner.expires_at <= now()
            elif room.state in {"ready", "running"}:
                expired |= not room.heartbeat_at or room.heartbeat_at < now() - timedelta(
                    seconds=40
                )
            if room.state != "waiting" and room.state != "running":
                expired |= room.launch_deadline is None or room.launch_deadline <= now()
            if expired:
                room.state = "closed"
            if (
                app.state.settings.relay_enabled
                and room.state in {"starting", "ready", "running"}
                and (room.id, 0) not in app.state.relay.slots
            ):
                room.state = "closed"
        await db.commit()
        if app.state.settings.relay_enabled:
            await app.state.relay.prune(db)


def install(app):
    @app.get("/v1/rooms")
    async def rooms(auth: Auth, db: DB):
        items = await db.scalars(
            select(Room)
            .where(Room.state.in_(["waiting", "hosting", "queued", "starting", "ready", "running"]))
            .order_by(Room.created_at)
            .limit(50)
        )
        views = []
        for room in items:
            views.append(await view(db, room))
        return {
            "rooms": views,
            "build_id": app.state.settings.game_build_id,
        }

    @app.post("/v1/rooms", status_code=201)
    async def create(data: NewRoom, request: Request, auth: Auth, db: DB):
        settings = app.state.settings
        if (
            settings.game_hosting == "dedicated" and not settings.game_executable
        ) or data.build_id != settings.game_build_id:
            raise HTTPException(409, "Game server build is not available")
        try:
            name = validate_nickname(data.name)
        except ValueError:
            raise HTTPException(422, "Room name must be 2-16 valid characters") from None
        await throttle(request, [(f"room_create:{auth[0].id}", 5)])
        await recheck_locked(db, auth)
        parent_id = await room_parent_session_id(db, auth)
        # Global allocation lock also serializes capacity with the worker
        await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
        await leave_other_rooms(db, auth[0].id)
        count = await db.scalar(
            select(func.count()).select_from(Room).where(Room.state != "closed")
        )
        if count >= settings.game_max_rooms:
            raise HTTPException(409, "Local server capacity reached")
        room = Room(
            owner_id=auth[0].id,
            hosting=settings.game_hosting,
            port=settings.game_port_start if settings.game_hosting == "listen" else None,
            name=name,
            build_id=data.build_id,
            state="waiting",
            created_at=now(),
        )
        db.add(room)
        await db.flush()
        db.add(
            RoomMember(
                room_id=room.id,
                account_id=auth[0].id,
                session_id=auth[1].id,
                launcher_session_id=parent_id,
                expires_at=now() + timedelta(seconds=45),
            )
        )
        await db.commit()
        return await view(db, room)

    @app.post("/v1/rooms/{room_id}/lobby")
    async def lobby(room_id: uuid.UUID, data: LobbyAction, auth: Auth, db: DB):
        await recheck_locked(db, auth)
        parent_id = await room_parent_session_id(db, auth)
        await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
        room = await db.get(Room, room_id, with_for_update=True)
        if room is None or room.state == "closed":
            raise HTTPException(404, "Room not found")
        member = await db.get(RoomMember, (room_id, auth[0].id))
        if data.action == "join":
            if room.state != "waiting":
                raise HTTPException(409, "Party has already departed")
            people = await live_members(db, room)
            if not any(m.account_id == room.owner_id for m, _ in people):
                raise HTTPException(409, "Party owner is no longer present")
            if not any(m.account_id == auth[0].id for m, _ in people) and len(people) >= 3:
                raise HTTPException(409, "Room is full")
            await leave_other_rooms(db, auth[0].id, room_id)
            if member is None:
                member = RoomMember(
                    room_id=room_id,
                    account_id=auth[0].id,
                    session_id=auth[1].id,
                    expires_at=now() + timedelta(seconds=45),
                )
                db.add(member)
            member.launcher_session_id = parent_id
            member.session_id = auth[1].id
            member.ready = False
        elif (
            member is None or member.launcher_session_id != parent_id or member.expires_at <= now()
        ):
            raise HTTPException(409, "Join this party again")
        if data.action == "leave":
            if room.state != "waiting":
                raise HTTPException(409, "Party is departing; wait for the game to connect")
            if room.owner_id == auth[0].id:
                room.state = "closed"
            await db.delete(member)
        else:
            if data.action not in {"join", "pulse"} and room.state != "waiting":
                raise HTTPException(409, "Party settings are locked for departure")
            if room.state == "waiting":
                member.expires_at = now() + timedelta(seconds=45)
            if data.action == "preference":
                member.preference = data.preference
                member.ready = False
            elif data.action == "ready":
                if data.enabled and member.preference is None:
                    raise HTTPException(409, "Choose a weapon first")
                member.ready = data.enabled
            elif data.action in {"bots", "start"}:
                if room.owner_id != auth[0].id:
                    raise HTTPException(403, "Party owner required")
                people = await live_members(db, room)
                if data.action == "bots":
                    room.fill_with_bots = data.enabled
                    for peer, _ in people:
                        peer.ready = False
                else:
                    if (
                        not people
                        or (not room.fill_with_bots and len(people) != 3)
                        or any(not m.ready or m.preference is None for m, _ in people)
                    ):
                        raise HTTPException(
                            409, "All party members must choose a weapon and be ready"
                        )
                    room.launch_members = [
                        dict(account_id=str(m.account_id), preference=m.preference)
                        for m, _ in people
                    ]
                    room.state = "hosting" if room.hosting == "listen" else "queued"
                    room.launch_deadline = now() + timedelta(seconds=180)
                    room.heartbeat_at = now()
                    for peer, _ in people:
                        peer.expires_at = now() + timedelta(seconds=180)
        await db.commit()
        return await view(db, room)

    @app.post("/v1/rooms/{room_id}/host")
    async def claim_host(room_id: uuid.UUID, request: Request, auth: Auth, db: DB):
        if auth[1].kind != "game":
            raise HTTPException(403, "Game session required")
        await recheck_locked(db, auth)
        room = await db.get(Room, room_id, with_for_update=True)
        member = await db.get(RoomMember, (room_id, auth[0].id))
        if (
            not room
            or room.hosting != "listen"
            or room.owner_id != auth[0].id
            or not member
            or member.launcher_session_id != auth[1].parent_id
        ):
            raise HTTPException(403, "Departed party owner required")
        if room.state != "hosting" or not room.launch_deadline or room.launch_deadline <= now():
            raise HTTPException(409, "Host startup already claimed or expired")
        # Use the observed peer, never a caller-supplied redirect target
        address = ipaddress.ip_address(
            app.state.settings.relay_public_address
            if app.state.settings.relay_enabled
            else request.client.host
        )
        if address.version != 4 or address.is_unspecified or address.is_multicast:
            raise HTTPException(409, "IPv4 host address required")
        key = token()
        room.server_hash = digest(key)
        room.host_session_id = auth[1].id
        room.address = str(address)
        room.state = "starting"
        room.heartbeat_at = now()
        relay = {"enabled": False}
        if app.state.settings.relay_enabled:
            relay = app.state.relay.issue(room.id, auth[1].id, auth[0].id, 0)
        try:
            await db.commit()
        except BaseException:
            allocated = app.state.relay.slots.get((room.id, 0))
            if allocated:
                app.state.relay.remove(allocated)
            raise
        return {"server_key": key, "port": room.port, "relay": relay}

    @app.post("/v1/rooms/{room_id}/relay")
    async def relay_credentials(room_id: uuid.UUID, auth: Auth, db: DB):
        if auth[1].kind != "game":
            raise HTTPException(403, "Game session required")
        await recheck_locked(db, auth)
        room = await db.get(Room, room_id, with_for_update=True)
        member = await db.get(RoomMember, (room_id, auth[0].id))
        if (
            not room
            or room.state not in {"ready", "running"}
            or not member
            or member.launcher_session_id != auth[1].parent_id
            or member.expires_at <= now()
            or (room.hosting == "listen" and auth[0].id == room.owner_id)
            or str(auth[0].id) not in {m["account_id"] for m in room.launch_members or []}
        ):
            raise HTTPException(403, "Departed party member required")
        if not app.state.settings.relay_enabled or room.hosting == "dedicated":
            return {"enabled": False}
        guests = [
            m["account_id"] for m in room.launch_members if m["account_id"] != str(room.owner_id)
        ]
        return app.state.relay.issue(
            room.id, auth[1].id, auth[0].id, guests.index(str(auth[0].id)) + 1
        )

    @app.delete("/v1/rooms/{room_id}")
    async def close(room_id: uuid.UUID, auth: Auth, db: DB):
        await recheck_locked(db, auth)
        parent_id = await room_parent_session_id(db, auth)
        await db.execute(text("SELECT pg_advisory_xact_lock(74621001)"))
        room = await db.get(Room, room_id, with_for_update=True)
        if room is None or room.owner_id != auth[0].id:
            raise HTTPException(404, "Room not found")
        if room.state == "closed":
            return {"closed": True}
        if auth[1].kind == "game":
            member = await db.get(RoomMember, (room_id, auth[0].id))
            if member is None or member.launcher_session_id != parent_id:
                raise HTTPException(403, "Departed party owner required")
        room.state = "closed"
        await db.commit()
        return {"closed": True}

    @app.post("/v1/rooms/{room_id}/tickets")
    async def issue(room_id: uuid.UUID, data: TicketRequest, request: Request, auth: Auth, db: DB):
        if auth[1].kind != "game":
            raise HTTPException(403, "Game session required")
        await throttle(request, [(f"ticket:{auth[0].id}", 20)])
        await recheck_locked(db, auth)
        room = await db.get(Room, room_id, with_for_update=True)
        host_starting = (
            room is not None
            and room.hosting == "listen"
            and room.state == "starting"
            and room.host_session_id == auth[1].id
        )
        if (
            room is None
            or (room.state not in {"ready", "running"} and not host_starting)
            or room.build_id != data.build_id
        ):
            raise HTTPException(409, "Room or build is not ready")
        if not room.heartbeat_at or room.heartbeat_at < now() - timedelta(seconds=35):
            raise HTTPException(409, "Game server unavailable")
        stale = [RoomMember.room_id == room_id, RoomMember.expires_at <= now()]
        if room.launch_members:
            # Departed accounts keep their reservation; the run is theirs until it ends
            stale.append(
                RoomMember.account_id.not_in({int(m["account_id"]) for m in room.launch_members})
            )
        await db.execute(delete(RoomMember).where(*stale))
        member = await db.get(RoomMember, (room_id, auth[0].id))
        if room.launch_members is not None and (
            not member
            or member.launcher_session_id != auth[1].parent_id
            or str(auth[0].id) not in {m["account_id"] for m in room.launch_members}
        ):
            raise HTTPException(403, "Account is not in the departed party")
        if member is None:
            if room.state == "running":
                raise HTTPException(409, "Run already started")
            count = await db.scalar(
                select(func.count()).select_from(RoomMember).where(RoomMember.room_id == room_id)
            )
            owner_reserved = await db.get(RoomMember, (room_id, room.owner_id))
            capacity = 3 if auth[0].id == room.owner_id or owner_reserved else 2
            if count >= capacity:
                raise HTTPException(409, "Room is full")
            member = RoomMember(
                room_id=room_id,
                account_id=auth[0].id,
                session_id=auth[1].id,
                expires_at=now() + timedelta(seconds=45),
            )
            db.add(member)
        if member.session_id != auth[1].id:
            member.connected = False
        member.session_id = auth[1].id
        member.expires_at = max(member.expires_at, now() + timedelta(seconds=45))
        raw = token()
        db.add(
            JoinTicket(
                token_hash=digest(raw),
                room_id=room_id,
                account_id=auth[0].id,
                session_id=auth[1].id,
                challenge=data.challenge,
                expires_at=now() + timedelta(seconds=20),
            )
        )
        await db.commit()
        return {"ticket": raw, "expires_in": 20}

    @app.post("/internal/rooms/{room_id}/redeem")
    async def redeem(
        room_id: uuid.UUID,
        data: Redeem,
        db: DB,
        x_server_key: Annotated[str | None, Header()] = None,
    ):
        await server_room(db, room_id, x_server_key)
        ticket = await db.get(JoinTicket, digest(data.ticket.get_secret_value()))
        if ticket is None or ticket.room_id != room_id:
            raise HTTPException(401, "Invalid ticket")
        account = await lock_account(db, ticket.account_id)
        room = await db.get(Room, room_id, with_for_update=True, populate_existing=True)
        await db.refresh(ticket, with_for_update=True)
        session = await db.get(AuthSession, ticket.session_id)
        if (
            ticket.consumed_at is not None
            or ticket.expires_at <= now()
            or ticket.challenge != data.challenge
            or room.build_id != data.build_id
            or (
                room.state not in {"ready", "running"}
                and not (
                    room.hosting == "listen"
                    and room.state == "starting"
                    and room.host_session_id == session.id
                )
            )
            or not await session_valid(db, account, session)
        ):
            raise HTTPException(401, "Invalid ticket")
        member = await db.get(RoomMember, (room_id, account.id))
        if member is None or member.session_id != session.id or member.expires_at <= now():
            raise HTTPException(401, "Reservation expired")
        ticket.consumed_at = now()
        member.expires_at = now() + timedelta(seconds=90)
        member.connected = True
        await db.commit()
        return {
            "account_id": str(account.id),
            "nickname": account.nickname,
            "session_id": str(session.id),
            "is_owner": room.owner_id == account.id,
        }

    @app.post("/internal/rooms/{room_id}/heartbeat")
    async def heartbeat(
        room_id: uuid.UUID,
        data: Heartbeat,
        db: DB,
        x_server_key: Annotated[str | None, Header()] = None,
    ):
        await server_room(db, room_id, x_server_key)
        room = await db.get(Room, room_id, with_for_update=True, populate_existing=True)
        if room.state == "closed":
            return {"closed": True, "revoked": data.accounts}
        room.state = data.state
        room.heartbeat_at = now()
        revoked = []
        claims = {claim.account_id: claim.session_id for claim in data.sessions}
        departed = {m["account_id"] for m in room.launch_members or []}
        # Keep in-flight reservations, but remove established peers absent from this heartbeat
        for member in list(
            await db.scalars(select(RoomMember).where(RoomMember.room_id == room_id))
        ):
            if str(member.account_id) in data.accounts:
                continue
            # Redemption can finish after the server captured this heartbeat's list
            # Preserve freshly confirmed peers through one full heartbeat interval
            if member.connected and member.expires_at < now() + timedelta(seconds=75):
                if room.state != "running":
                    await db.delete(member)
                    continue
                member.connected = False
            # A live run holds the seat of every account that departed with this party,
            # so the same person can rejoin the host they left instead of losing the run
            if (
                room.state == "running"
                and not member.connected
                and str(member.account_id) in departed
            ):
                member.expires_at = now() + timedelta(seconds=90)
        for raw in data.accounts:
            if not raw.isascii() or not raw.isdigit() or len(raw) > 19:
                raise HTTPException(422, "Invalid account")
            account_id = int(raw)
            member = await db.get(RoomMember, (room_id, account_id))
            account = await db.get(Account, account_id)
            session_id = claims.get(raw)
            session = await db.get(AuthSession, session_id) if session_id else None
            if (
                member is None
                or account is None
                or session is None
                or session.account_id != account_id
                or session.kind != "game"
                or member.session_id != session.id
                or not await session_valid(db, account, session)
            ):
                revoked.append(raw)
                if member and member.session_id == session_id:
                    if room.state == "running" and raw in departed:
                        # Cut the connection, not the seat. This account departed with the
                        # party and may still come back with a session that does check out
                        member.connected = False
                    else:
                        await db.delete(member)
            else:
                member.expires_at = now() + timedelta(seconds=90)
        await db.commit()
        return {
            "closed": False,
            "revoked": revoked,
            "launch_members": room.launch_members,
            "fill_with_bots": room.fill_with_bots,
        }
