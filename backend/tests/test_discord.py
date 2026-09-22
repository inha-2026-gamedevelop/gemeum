import asyncio
from datetime import timedelta

import httpx
import pytest
from fastapi import HTTPException
from sqlalchemy import select

from geumeum_backend.discord_accounts import issue_ticket
from geumeum_backend.discord_delivery import deliver_one
from geumeum_backend.models import Account, DiscordIdentity, DiscordTicket, NotificationOutbox
from geumeum_backend.releases import NewNotice, record_notice
from geumeum_backend.security import digest, now

PASSWORD = "Discord123!"


async def proof(app, user=123):
    async with app.state.sessions() as db:
        return await issue_ticket(db, user)


def headers(raw):
    return {"Authorization": "Bearer " + raw, "Origin": "http://127.0.0.1:8766"}


async def create(app, client, user=123, login="discordtester"):
    raw = await proof(app, user)
    response = await client.post(
        "/discord/apply",
        headers=headers(raw),
        json={"action": "create", "login_id": login, "nickname": "새닉네임", "password": PASSWORD},
    )
    assert response.status_code == 200, response.text
    return response.json()["account"]


async def test_create_password_change_revokes_three_launcher_sessions(app, client):
    account = await create(app, client)
    sessions = []
    for _ in range(3):
        r = await client.post(
            "/v1/auth/login", json={"login_id": account["login_id"], "password": PASSWORD}
        )
        assert r.status_code == 200
        sessions.append(r.json())
    raw = await proof(app)
    r = await client.post(
        "/discord/apply",
        headers=headers(raw),
        json={"action": "password", "password": PASSWORD, "new_password": PASSWORD + "new"},
    )
    assert r.status_code == 200
    for session in sessions:
        r = await client.post("/v1/auth/refresh", json={"refresh_token": session["refresh_token"]})
        assert r.status_code == 401
    r = await client.post(
        "/v1/auth/login", json={"login_id": account["login_id"], "password": PASSWORD + "new"}
    )
    assert r.status_code == 200


async def test_ticket_origin_expiry_reissue_and_no_plaintext(app, client):
    raw = await proof(app)
    assert (
        await client.post("/discord/context", headers={"Authorization": "Bearer " + raw})
    ).status_code == 403
    newer = await proof(app)
    assert (await client.post("/discord/context", headers=headers(raw))).status_code == 401
    async with app.state.sessions() as db:
        item = await db.get(DiscordTicket, digest(newer))
        assert item.token_hash != newer
        item.expires_at = now() - timedelta(seconds=1)
        await db.commit()
    assert (await client.post("/discord/context", headers=headers(newer))).status_code == 401
    for _ in range(3):
        await proof(app)
    with pytest.raises(HTTPException) as exc:
        await proof(app)
    assert exc.value.status_code == 429


async def test_duplicate_discord_link_and_failed_password_limit(app, client):
    account = await create(app, client)
    raw = await proof(app, 456)
    for _ in range(5):
        r = await client.post(
            "/discord/apply",
            headers=headers(raw),
            json={"action": "link", "login_id": account["login_id"], "password": "wrong"},
        )
        assert r.status_code == 400
    r = await client.post(
        "/discord/apply",
        headers=headers(raw),
        json={"action": "link", "login_id": account["login_id"], "password": PASSWORD},
    )
    assert r.status_code == 401
    raw = await proof(app, 456)
    r = await client.post(
        "/discord/apply",
        headers=headers(raw),
        json={"action": "link", "login_id": account["login_id"], "password": PASSWORD},
    )
    assert r.status_code == 409
    async with app.state.sessions() as db:
        assert await db.get(DiscordIdentity, 456) is None
        stored = await db.get(Account, int(account["account_id"]))
        assert stored.password_hash.startswith("$argon2id$")
        assert PASSWORD not in stored.password_hash


async def test_parallel_ticket_consumption_only_one_change(app, client):
    await create(app, client)
    raw = await proof(app)
    responses = await asyncio.gather(
        *[
            client.post(
                "/discord/apply",
                headers=headers(raw),
                json={"action": "nickname", "nickname": name},
            )
            for name in ("첫번째", "두번째", "세번째")
        ]
    )
    assert sorted(r.status_code for r in responses) == [200, 401, 401]


async def test_three_existing_accounts_link_to_distinct_discord_users(app, client):
    from geumeum_backend.accounts import create_account
    from geumeum_backend.schemas import AccountCreate

    for index in range(3):
        async with app.state.sessions() as db:
            await create_account(
                db,
                app.state.passwords,
                AccountCreate(
                    login_id=f"existing{index}", nickname=f"기존유저{index}", password=PASSWORD
                ),
            )
        raw = await proof(app, 1000 + index)
        response = await client.post(
            "/discord/apply",
            headers=headers(raw),
            json={"action": "link", "login_id": f"existing{index}", "password": PASSWORD},
        )
        assert response.status_code == 200
        second = await client.post(
            "/discord/apply",
            headers=headers(raw),
            json={"action": "nickname", "nickname": "재사용"},
        )
        assert second.status_code == 401
    async with app.state.sessions() as db:
        identities = list(await db.scalars(select(DiscordIdentity)))
        assert len({item.account_id for item in identities}) == 3
        assert {item.discord_id for item in identities} == {1000, 1001, 1002}


@pytest.mark.parametrize(
    "code,state", [(200, "sent"), (403, "failed"), (429, "pending"), (500, "needs_review")]
)
async def test_delivery_states_and_no_mentions(app, code, state):
    async with app.state.sessions() as db:
        await record_notice(
            db,
            NewNotice(product="launcher", channel="stable", version="test", notes="@everyone Test"),
        )
        await db.commit()
    calls = []

    def respond(request):
        import json

        body = json.loads(request.content)
        assert body["allowed_mentions"] == {"parse": []}
        assert body["enforce_nonce"] is True
        calls.append(body)
        return httpx.Response(code, json={"id": "999", "channel_id": "111", "retry_after": 10})

    async with httpx.AsyncClient(
        base_url="https://discord.com/api/v10/", transport=httpx.MockTransport(respond)
    ) as http:
        assert await deliver_one(app.state.sessions, http, {"stable": 111})
        assert not await deliver_one(app.state.sessions, http, {"stable": 111})
    assert len(calls) == 1
    async with app.state.sessions() as db:
        item = await db.scalar(select(NotificationOutbox))
        assert item.state == state


async def test_crashed_delivery_and_internal_not_published(app):
    async with app.state.sessions() as db:
        await record_notice(
            db, NewNotice(product="game", channel="stable", version="crashed", notes="Test")
        )
        await db.flush()
        item = await db.scalar(select(NotificationOutbox))
        item.state = "sending"
        item.attempted_at = now() - timedelta(minutes=6)
        await record_notice(
            db, NewNotice(product="game", channel="internal", version="internal", notes="Test")
        )
        await db.commit()
    async with httpx.AsyncClient(base_url="https://discord.com/api/v10/") as http:
        assert not await deliver_one(app.state.sessions, http, {"stable": 111})
    async with app.state.sessions() as db:
        assert sorted(await db.scalars(select(NotificationOutbox.state))) == [
            "needs_review",
            "pending",
        ]


@pytest.mark.parametrize("size,expected", [(7, 422), (8, 200), (15, 200), (16, 422)])
async def test_discord_creation_password_boundaries(app, client, size, expected):
    raw = await proof(app)
    response = await client.post(
        "/discord/apply",
        headers=headers(raw),
        json={
            "action": "create",
            "login_id": "boundary",
            "nickname": "Boundary",
            "password": "x" * size,
        },
    )
    assert response.status_code == expected
    assert "x" * size not in response.text
