import asyncio
import uuid
from datetime import timedelta

from sqlalchemy import select
from test_accounts import PASSWORD, auth, login, seed

from geumeum_backend.models import JoinTicket, Room
from geumeum_backend.security import digest, now


async def prepare(app, client):
    app.state.settings.game_executable = "local-test.exe"
    app.state.settings.game_build_id = "test-build"
    await seed(app)
    parent = await login(client)
    game = (await client.post("/v1/auth/game-sessions", headers=auth(parent))).json()
    response = await client.post(
        "/v1/rooms", headers=auth(parent), json={"name": "테스트방", "build_id": "test-build"}
    )
    assert response.status_code == 201
    room_id = response.json()["room_id"]
    async with app.state.sessions() as db:
        room = await db.get(Room, uuid.UUID(room_id))
        room.state, room.server_hash, room.heartbeat_at, room.port = (
            "ready",
            digest("server-secret"),
            now(),
            18000,
        )
        await db.commit()
    return parent, game, room_id


async def ticket(client, game, room_id, challenge="a" * 64):
    response = await client.post(
        f"/v1/rooms/{room_id}/tickets",
        headers=auth(game),
        json={"challenge": challenge, "build_id": "test-build"},
    )
    assert response.status_code == 200
    return response.json()["ticket"]


async def redeem(client, room_id, raw, challenge="a" * 64, key="server-secret"):
    return await client.post(
        f"/internal/rooms/{room_id}/redeem",
        headers={"X-Server-Key": key},
        json={"ticket": raw, "challenge": challenge, "build_id": "test-build"},
    )


async def test_ticket_is_one_use_and_bound_to_challenge_and_server(app, client):
    _, game, room = await prepare(app, client)
    raw = await ticket(client, game, room)
    assert (await redeem(client, room, raw, key="wrong")).status_code == 401
    assert (await redeem(client, room, raw, challenge="b" * 64)).status_code == 401
    results = await asyncio.gather(redeem(client, room, raw), redeem(client, room, raw))
    assert sorted(r.status_code for r in results) == [200, 401]
    accepted = next(r for r in results if r.status_code == 200)
    assert accepted.json()["account_id"] == game["account"]["account_id"]


async def test_revocation_checked_on_redeem_and_heartbeat(app, client):
    parent, game, room = await prepare(app, client)
    raw = await ticket(client, game, room)
    identity = (await redeem(client, room, raw)).json()
    beat = {
        "state": "ready",
        "accounts": [identity["account_id"]],
        "sessions": [{"account_id": identity["account_id"], "session_id": identity["session_id"]}],
    }
    assert (
        await client.post(
            f"/internal/rooms/{room}/heartbeat",
            headers={"X-Server-Key": "server-secret"},
            json=beat,
        )
    ).json()["revoked"] == []
    raw = await ticket(client, game, room)
    await client.post("/v1/auth/logout", headers=auth(parent))
    assert (await redeem(client, room, raw)).status_code == 401
    response = await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": "server-secret"},
        json=beat,
    )
    assert response.json()["revoked"] == [game["account"]["account_id"]]


async def test_new_reservation_cannot_hide_revoked_connection_session(app, client):
    parent, game, room = await prepare(app, client)
    old = (await redeem(client, room, await ticket(client, game, room))).json()
    next_parent = await login(client)
    next_game = (await client.post("/v1/auth/game-sessions", headers=auth(next_parent))).json()
    await ticket(client, next_game, room)
    await client.post("/v1/auth/logout", headers=auth(parent))
    response = await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": "server-secret"},
        json={
            "state": "ready",
            "accounts": [old["account_id"]],
            "sessions": [{"account_id": old["account_id"], "session_id": old["session_id"]}],
        },
    )
    assert response.json()["revoked"] == [old["account_id"]]


async def test_expiry_build_and_session_scope(app, client):
    parent, game, room = await prepare(app, client)
    request = {"challenge": "a" * 64, "build_id": "wrong"}
    assert (
        await client.post(f"/v1/rooms/{room}/tickets", headers=auth(game), json=request)
    ).status_code == 409
    request["build_id"] = "test-build"
    assert (
        await client.post(f"/v1/rooms/{room}/tickets", headers=auth(parent), json=request)
    ).status_code == 403
    raw = await ticket(client, game, room)
    async with app.state.sessions() as db:
        row = await db.scalar(select(JoinTicket))
        row.expires_at = now() - timedelta(seconds=1)
        await db.commit()
    assert (await redeem(client, room, raw)).status_code == 401


async def test_room_capacity_and_owner_close(app, client):
    parent, _, room = await prepare(app, client)
    assert (
        await client.post(
            "/v1/rooms", headers=auth(parent), json={"name": "두번째방", "build_id": "test-build"}
        )
    ).status_code == 409
    response = await client.delete(f"/v1/rooms/{room}", headers=auth(parent))
    assert response.status_code == 200
    response = await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": "server-secret"},
        json={"state": "ready", "accounts": []},
    )
    assert response.json()["closed"]


async def test_four_accounts_racing_reserve_only_three_seats(app, client):
    _, first, room = await prepare(app, client)
    games = [first]
    for i in range(3):
        await seed(app, login=f"guest{i}", nickname=f"Guest{i}")
        parent = (
            await client.post(
                "/v1/auth/login", json={"login_id": f"guest{i}", "password": PASSWORD}
            )
        ).json()
        games.append((await client.post("/v1/auth/game-sessions", headers=auth(parent))).json())
    replies = await asyncio.gather(
        *[
            client.post(
                f"/v1/rooms/{room}/tickets",
                headers=auth(game),
                json={"challenge": "a" * 64, "build_id": "test-build"},
            )
            for game in games
        ]
    )
    assert sorted(reply.status_code for reply in replies) == [200, 200, 200, 409]


async def test_room_roster_identifies_owner_and_removes_disconnected(app, client):
    parent, game, room = await prepare(app, client)
    identity = (await redeem(client, room, await ticket(client, game, room))).json()
    assert identity["is_owner"] is True
    view = (await client.get("/v1/rooms", headers=auth(parent))).json()["rooms"][0]
    assert view["owner_id"] == parent["account"]["account_id"]
    assert view["members"][0]["nickname"] == parent["account"]["nickname"]
    assert view["members"][0]["connected"] is True
    assert view["members"][0]["is_owner"] is True
    from geumeum_backend.models import RoomMember

    async with app.state.sessions() as db:
        member = await db.get(RoomMember, (uuid.UUID(room), int(parent["account"]["account_id"])))
        member.expires_at = now() + timedelta(seconds=70)
        await db.commit()
    await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": "server-secret"},
        json={"state": "ready", "accounts": [], "sessions": []},
    )
    view = (await client.get("/v1/rooms", headers=auth(parent))).json()["rooms"][0]
    assert view["members"] == []


async def test_guest_identity_cannot_become_room_owner(app, client):
    parent, _, room = await prepare(app, client)
    await seed(app, login="guest", nickname="Guest")
    guest = (
        await client.post("/v1/auth/login", json={"login_id": "guest", "password": PASSWORD})
    ).json()
    game = (await client.post("/v1/auth/game-sessions", headers=auth(guest))).json()
    identity = (await redeem(client, room, await ticket(client, game, room))).json()
    assert identity["is_owner"] is False
    assert (await client.delete(f"/v1/rooms/{room}", headers=auth(guest))).status_code == 404
    view = (await client.get("/v1/rooms", headers=auth(parent))).json()["rooms"][0]
    assert (
        next(m for m in view["members"] if m["account_id"] == guest["account"]["account_id"])[
            "is_owner"
        ]
        is False
    )


async def test_heartbeat_preserves_new_arrival_and_running_reconnect(app, client):
    from geumeum_backend.models import RoomMember

    parent, game, room = await prepare(app, client)
    await redeem(client, room, await ticket(client, game, room))
    endpoint = f"/internal/rooms/{room}/heartbeat"
    headers = {"X-Server-Key": "server-secret"}
    await client.post(endpoint, headers=headers, json={"state": "ready", "accounts": []})
    view = (await client.get("/v1/rooms", headers=auth(parent))).json()["rooms"][0]
    assert view["members"][0]["connected"] is True
    async with app.state.sessions() as db:
        member = await db.get(RoomMember, (uuid.UUID(room), int(parent["account"]["account_id"])))
        member.expires_at = now() + timedelta(seconds=70)
        await db.commit()
    await client.post(endpoint, headers=headers, json={"state": "running", "accounts": []})
    view = (await client.get("/v1/rooms", headers=auth(parent))).json()["rooms"][0]
    assert view["members"][0]["connected"] is False
    assert (await redeem(client, room, await ticket(client, game, room))).status_code == 200
