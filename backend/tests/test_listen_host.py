import asyncio
import uuid
from datetime import timedelta

import httpx
import pytest
from pydantic import ValidationError
from test_accounts import auth
from test_launcher_lobby import action, guest, setup_party

from geumeum_backend.api import create_app
from geumeum_backend.config import Settings
from geumeum_backend.models import Room
from geumeum_backend.security import now


async def depart(app, client):
    app.state.settings.game_hosting = "listen"
    owner, room = await setup_party(app, client)
    app.state.settings.game_executable = ""
    await action(client, owner, room, "bots", enabled=True)
    await action(client, owner, room, "preference", preference=0)
    await action(client, owner, room, "ready", enabled=True)
    assert (await action(client, owner, room, "start")).json()["state"] == "hosting"
    game = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    return owner, game, room


async def test_only_owner_game_can_claim_once_and_revoke_host(app, client):
    owner, game, room = await depart(app, client)
    endpoint = f"/v1/rooms/{room}/host"
    assert (await client.post(endpoint, headers=auth(owner))).status_code == 403
    stranger = await guest(app, client, "stranger")
    stranger_game = (await client.post("/v1/auth/game-sessions", headers=auth(stranger))).json()
    assert (await client.post(endpoint, headers=auth(stranger_game))).status_code == 403
    replies = await asyncio.gather(*[client.post(endpoint, headers=auth(game)) for _ in range(2)])
    assert sorted(r.status_code for r in replies) == [200, 409]
    key = next(r.json()["server_key"] for r in replies if r.status_code == 200)
    # The local host must authenticate before the first readiness heartbeat
    request = {"challenge": "a" * 64, "build_id": "test-build"}
    ticket = await client.post(f"/v1/rooms/{room}/tickets", headers=auth(game), json=request)
    assert ticket.status_code == 200
    redeemed = await client.post(
        f"/internal/rooms/{room}/redeem",
        headers={"X-Server-Key": key},
        json={**request, "ticket": ticket.json()["ticket"]},
    )
    assert redeemed.status_code == 200
    assert redeemed.json()["is_owner"]
    beat = f"/internal/rooms/{room}/heartbeat"
    assert (
        await client.post(
            beat, headers={"X-Server-Key": key}, json={"state": "ready", "accounts": []}
        )
    ).status_code == 200
    view = (await client.get("/v1/rooms", headers=auth(owner))).json()["rooms"][0]
    assert view["hosting"] == "listen" and view["address"] == "127.0.0.1"
    assert "server_key" not in view
    await client.post("/v1/auth/logout", headers=auth(game))
    assert (
        await client.post(
            beat, headers={"X-Server-Key": key}, json={"state": "ready", "accounts": []}
        )
    ).status_code == 401


async def test_host_start_timeout_expires_without_game_worker(app, client):
    owner, _, room = await depart(app, client)
    async with app.state.sessions() as db:
        record = await db.get(Room, uuid.UUID(room))
        record.launch_deadline = now() - timedelta(seconds=1)
        await db.commit()
    for _ in range(30):
        views = (await client.get("/v1/rooms", headers=auth(owner))).json()["rooms"]
        if not views:
            break
        await asyncio.sleep(0.1)
    assert not views


async def test_production_origin_https_and_proxy_boundary(test_url):
    settings = Settings(
        environment="production",
        database_url=test_url,
        public_origin="https://api.example.com",
        game_build_id="test-build",
    )
    app = create_app(settings)
    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app, client=("203.0.113.4", 12000))
        ) as client:
            assert (await client.get("https://api.example.com/health/ready")).status_code == 200
            assert (await client.get("http://api.example.com/health/ready")).status_code == 403
            assert (await client.get("https://wrong.example.com/health/ready")).status_code == 400
            # Caller-supplied forwarding headers cannot change the ASGI client/scheme
            assert (
                await client.get(
                    "http://api.example.com/health/ready", headers={"X-Forwarded-Proto": "https"}
                )
            ).status_code == 403
    assert settings.account_origin == "https://api.example.com"
    with pytest.raises(ValidationError):
        Settings(
            environment="production",
            database_url=test_url,
            public_origin="http://api.example.com",
            game_build_id="test-build",
        )
