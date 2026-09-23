import asyncio
import uuid
from datetime import timedelta

from sqlalchemy import select
from test_accounts import PASSWORD, auth, login, seed

from geumeum_backend.models import AuthSession, Room, RoomMember
from geumeum_backend.security import digest, now


async def setup_party(app, client):
    app.state.settings.game_executable = "test.exe"
    app.state.settings.game_build_id = "test-build"
    await seed(app)
    owner = await login(client)
    reply = await client.post(
        "/v1/rooms", headers=auth(owner), json={"name": "출발준비", "build_id": "test-build"}
    )
    assert reply.status_code == 201
    assert reply.json()["state"] == "waiting"
    assert len(reply.json()["members"]) == 1
    return owner, reply.json()["room_id"]


async def action(client, user, room, verb, **kw):
    return await client.post(
        f"/v1/rooms/{room}/lobby", headers=auth(user), json={"action": verb, **kw}
    )


async def guest(app, client, name):
    await seed(app, login=name, nickname=name)
    return (
        await client.post("/v1/auth/login", json={"login_id": name, "password": PASSWORD})
    ).json()


async def test_game_session_can_prepare_but_departure_still_requires_ready(app, client):
    owner, room = await setup_party(app, client)
    game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    assert (await action(client, game, room, "pulse")).status_code == 200
    assert (await action(client, game, room, "ready", enabled=True)).status_code == 409
    assert (await action(client, game, room, "start")).status_code == 409
    assert (
        await client.post(
            f"/v1/rooms/{room}/tickets",
            headers=auth(game),
            json={"challenge": "a" * 64, "build_id": "test-build"},
        )
    ).status_code == 409


async def test_game_sessions_create_join_prepare_and_keep_launcher_parent(app, client):
    app.state.settings.game_executable = "test.exe"
    app.state.settings.game_build_id = "test-build"
    await seed(app)
    owner = await login(client)
    owner_game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    reply = await client.post(
        "/v1/rooms", headers=auth(owner_game), json={"name": "게임로비", "build_id": "test-build"}
    )
    assert reply.status_code == 201
    room = reply.json()["room_id"]
    peer = await guest(app, client, "guest1")
    peer_game = (await client.post("/v1/auth/game-sessions", headers=auth(peer))).json()
    assert (await action(client, peer_game, room, "join")).status_code == 200
    assert (await action(client, peer_game, room, "preference", preference=1)).status_code == 200
    assert (await action(client, peer_game, room, "ready", enabled=True)).status_code == 200
    assert (await action(client, owner_game, room, "bots", enabled=True)).status_code == 200
    assert (await action(client, owner_game, room, "preference", preference=0)).status_code == 200
    assert (await action(client, owner_game, room, "ready", enabled=True)).status_code == 200
    assert (await action(client, peer_game, room, "ready", enabled=True)).status_code == 200
    assert (await action(client, owner_game, room, "start")).json()["state"] == "queued"
    async with app.state.sessions() as db:
        owner_session = await db.scalar(
            select(AuthSession).where(AuthSession.access_hash == digest(owner_game["access_token"]))
        )
        member = await db.get(RoomMember, (uuid.UUID(room), int(owner["account"]["account_id"])))
        assert member.session_id == owner_session.id
        assert member.launcher_session_id == owner_session.parent_id


async def test_game_room_actions_require_matching_parent_and_live_parent(app, client):
    owner, room = await setup_party(app, client)
    first_game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    other_parent = await login(client)
    other_game = (await client.post("/v1/auth/game-sessions", headers=auth(other_parent))).json()
    assert (await action(client, first_game, room, "pulse")).status_code == 200
    assert (await action(client, other_game, room, "pulse")).status_code == 409
    assert (await client.delete(f"/v1/rooms/{room}", headers=auth(other_game))).status_code == 403
    assert (await client.delete(f"/v1/rooms/{room}", headers=auth(first_game))).status_code == 200
    repeated_close = await client.delete(f"/v1/rooms/{room}", headers=auth(first_game))
    assert repeated_close.json() == {"closed": True}
    assert (await action(client, first_game, room, "pulse")).status_code == 404

    new_room = (await client.post(
        "/v1/rooms", headers=auth(first_game), json={"name": "유효한방", "build_id": "test-build"}
    )).json()["room_id"]
    async with app.state.sessions() as db:
        game = await db.scalar(
            select(AuthSession).where(AuthSession.access_hash == digest(first_game["access_token"]))
        )
        parent = await db.get(AuthSession, game.parent_id)
        parent.revoked_at = now()
        await db.commit()
    assert (await action(client, first_game, new_room, "pulse")).status_code == 401
    assert (await client.post(
        "/v1/rooms", headers=auth(first_game), json={"name": "차단된방", "build_id": "test-build"}
    )).status_code == 401


async def test_game_room_actions_reject_unrelated_parent_session(app, client):
    owner, room = await setup_party(app, client)
    game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    peer = await guest(app, client, "guest1")
    async with app.state.sessions() as db:
        game_row = await db.scalar(
            select(AuthSession).where(AuthSession.access_hash == digest(game["access_token"]))
        )
        peer_row = await db.scalar(
            select(AuthSession).where(AuthSession.access_hash == digest(peer["access_token"]))
        )
        game_row.parent_id = peer_row.id
        await db.commit()
    assert (await action(client, game, room, "pulse")).status_code == 403
    assert (await client.post(
        "/v1/rooms", headers=auth(game), json={"name": "다른부모", "build_id": "test-build"}
    )).status_code == 403
    assert (await client.delete(f"/v1/rooms/{room}", headers=auth(game))).status_code == 403


async def test_owner_bot_party_freezes_and_binds_game_to_parent(app, client):
    owner, room = await setup_party(app, client)
    await action(client, owner, room, "bots", enabled=True)
    await action(client, owner, room, "preference", preference=2)
    await action(client, owner, room, "ready", enabled=True)
    assert (await action(client, owner, room, "start")).json()["state"] == "queued"
    assert (await action(client, owner, room, "preference", preference=0)).status_code == 409
    assert (await action(client, owner, room, "leave")).status_code == 409
    outsider = await guest(app, client, "outsider")
    assert (await action(client, outsider, room, "join")).status_code == 409
    async with app.state.sessions() as db:
        record = await db.get(Room, uuid.UUID(room))
        assert record.launch_members == [
            {"account_id": owner["account"]["account_id"], "preference": 2}
        ]
        record.state, record.server_hash, record.heartbeat_at = (
            "ready",
            digest("server-secret"),
            now(),
        )
        await db.commit()
    game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    req = {"challenge": "a" * 64, "build_id": "test-build"}
    assert (
        await client.post(f"/v1/rooms/{room}/tickets", headers=auth(game), json=req)
    ).status_code == 200
    other_parent = await login(client)
    other_game = (await client.post("/v1/auth/game-sessions", headers=auth(other_parent))).json()
    assert (
        await client.post(f"/v1/rooms/{room}/tickets", headers=auth(other_game), json=req)
    ).status_code == 403


async def test_guest_permissions_readiness_reset_and_browse_keeps_membership(app, client):
    owner, room = await setup_party(app, client)
    peer = await guest(app, client, "guest1")
    await action(client, peer, room, "join")
    assert (await action(client, peer, room, "bots", enabled=True)).status_code == 403
    assert (await action(client, peer, room, "start")).status_code == 403
    await action(client, peer, room, "preference", preference=1)
    await action(client, peer, room, "ready", enabled=True)
    views = (await client.get("/v1/rooms", headers=auth(peer))).json()["rooms"]
    assert any(m["ready"] for m in views[0]["members"])
    await action(client, owner, room, "bots", enabled=True)
    assert all(
        not m["ready"] for m in (await action(client, peer, room, "pulse")).json()["members"]
    )
    await action(client, peer, room, "ready", enabled=True)
    reply = await action(client, peer, room, "preference", preference=0)
    assert not next(m for m in reply.json()["members"] if not m["is_owner"])["ready"]


async def test_switch_rooms_is_atomic_and_owner_leaving_closes_party(app, client):
    owner, room = await setup_party(app, client)
    peer = await guest(app, client, "guest1")
    target = (
        await client.post(
            "/v1/rooms", headers=auth(peer), json={"name": "다른파티", "build_id": "test-build"}
        )
    ).json()["room_id"]
    await action(client, owner, target, "join")
    async with app.state.sessions() as db:
        assert (await db.get(Room, uuid.UUID(room))).state == "closed"
        assert (
            await db.get(RoomMember, (uuid.UUID(room), int(owner["account"]["account_id"]))) is None
        )
    await action(client, owner, target, "leave")
    assert len((await action(client, peer, target, "pulse")).json()["members"]) == 1


async def test_concurrent_join_cannot_exceed_three(app, client):
    owner, room = await setup_party(app, client)
    peers = [await guest(app, client, "guest" + str(i)) for i in range(3)]
    replies = await asyncio.gather(*(action(client, peer, room, "join") for peer in peers))
    assert sorted(r.status_code for r in replies) == [200, 200, 409]
    assert len((await action(client, owner, room, "pulse")).json()["members"]) == 3


async def test_expired_or_revoked_participant_cannot_depart(app, client):
    owner, room = await setup_party(app, client)
    await action(client, owner, room, "preference", preference=0)
    await action(client, owner, room, "ready", enabled=True)
    async with app.state.sessions() as db:
        row = await db.get(RoomMember, (uuid.UUID(room), int(owner["account"]["account_id"])))
        row.expires_at = now() - timedelta(seconds=1)
        await db.commit()
    assert (await action(client, owner, room, "start")).status_code == 409


async def test_failed_switch_keeps_original_party(app, client):
    owner, original = await setup_party(app, client)
    peer = await guest(app, client, "otherowner")
    target = (
        await client.post(
            "/v1/rooms", headers=auth(peer), json={"name": "만석파티", "build_id": "test-build"}
        )
    ).json()["room_id"]
    for name in ("guest1", "guest2"):
        await action(client, await guest(app, client, name), target, "join")
    assert (await action(client, owner, target, "join")).status_code == 409
    assert (await action(client, owner, original, "pulse")).json()["state"] == "waiting"
