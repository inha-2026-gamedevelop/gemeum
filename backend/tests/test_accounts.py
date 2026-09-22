import asyncio
from datetime import timedelta

import httpx
import pytest
from fastapi import HTTPException
from pydantic import ValidationError
from sqlalchemy import select

from geumeum_backend.accounts import create_account
from geumeum_backend.config import Settings
from geumeum_backend.models import Account, AuthSession, RefreshToken
from geumeum_backend.schemas import AccountCreate, PasswordChange
from geumeum_backend.security import digest, now

PASSWORD = "LocalTest123!"


async def seed(app, login="tester", nickname="Tester"):
    async with app.state.sessions() as db:
        return await create_account(
            db,
            app.state.passwords,
            AccountCreate(login_id=login, nickname=nickname, password=PASSWORD),
        )


async def login(client):
    response = await client.post(
        "/v1/auth/login",
        json={
            "login_id": "  TESTER  ",
            "password": PASSWORD,
        },
    )
    assert response.status_code == 200, response.text
    return response.json()


def auth(tokens):
    return {"Authorization": "Bearer " + tokens["access_token"]}


async def test_login_profile_nickname_and_no_plaintext_storage(app, client):
    await seed(app)
    tokens = await login(client)
    assert isinstance(tokens["account"]["account_id"], str)
    result = await client.get("/v1/me", headers=auth(tokens))
    assert result.status_code == 200
    assert result.headers["cache-control"] == "no-store"
    changed = await client.patch("/v1/me/nickname", headers=auth(tokens), json={"nickname": "달빛"})
    assert changed.json()["nickname"] == "달빛"
    async with app.state.sessions() as db:
        account = await db.scalar(select(Account))
        session = await db.scalar(select(AuthSession))
        refresh = await db.scalar(select(RefreshToken))
        assert account.password_hash.startswith("$argon2id$")
        assert PASSWORD not in account.password_hash
        assert session.access_hash == digest(tokens["access_token"])
        assert refresh.token_hash == digest(tokens["refresh_token"])


async def test_refresh_rotation_and_replay_revokes_family(app, client):
    await seed(app)
    initial = await login(client)
    response = await client.post(
        "/v1/auth/refresh", json={"refresh_token": initial["refresh_token"]}
    )
    assert response.status_code == 200
    rotated = response.json()
    assert rotated["refresh_token"] != initial["refresh_token"]
    assert (await client.get("/v1/me", headers=auth(initial))).status_code == 401
    assert (await client.get("/v1/me", headers=auth(rotated))).status_code == 200
    replay = await client.post("/v1/auth/refresh", json={"refresh_token": initial["refresh_token"]})
    assert replay.status_code == 401
    assert (await client.get("/v1/me", headers=auth(rotated))).status_code == 401
    assert (
        await client.post(
            "/v1/auth/refresh",
            json={
                "refresh_token": rotated["refresh_token"],
            },
        )
    ).status_code == 401


async def test_concurrent_refresh_accepts_once_and_revokes_on_reuse(app, client):
    await seed(app)
    initial = await login(client)
    responses = await asyncio.gather(
        *[
            client.post("/v1/auth/refresh", json={"refresh_token": initial["refresh_token"]})
            for _ in range(2)
        ]
    )
    assert sorted(r.status_code for r in responses) == [200, 401]
    winner = next(r.json() for r in responses if r.status_code == 200)
    assert (await client.get("/v1/me", headers=auth(winner))).status_code == 401


async def test_password_change_invalidates_all_sessions(app, client):
    await seed(app)
    first, second = await login(client), await login(client)
    response = await client.post(
        "/v1/auth/password",
        headers=auth(first),
        json={
            "current_password": PASSWORD,
            "new_password": "NextTest456!",
        },
    )
    assert response.status_code == 204, response.text
    for tokens in (first, second):
        assert (await client.get("/v1/me", headers=auth(tokens))).status_code == 401
        assert (
            await client.post(
                "/v1/auth/refresh",
                json={
                    "refresh_token": tokens["refresh_token"],
                },
            )
        ).status_code == 401
    assert (
        await client.post(
            "/v1/auth/login",
            json={
                "login_id": "tester",
                "password": PASSWORD,
            },
        )
    ).status_code == 401
    assert (
        await client.post(
            "/v1/auth/login",
            json={
                "login_id": "tester",
                "password": "NextTest456!",
            },
        )
    ).status_code == 200


async def test_logout_is_scoped_and_logout_all_invalidates_other_sessions(app, client):
    await seed(app)
    first, second = await login(client), await login(client)
    assert (await client.post("/v1/auth/logout", headers=auth(first))).status_code == 204
    assert (await client.get("/v1/me", headers=auth(first))).status_code == 401
    assert (await client.get("/v1/me", headers=auth(second))).status_code == 200
    third = await login(client)
    assert (await client.post("/v1/auth/logout-all", headers=auth(second))).status_code == 204
    assert (await client.get("/v1/me", headers=auth(third))).status_code == 401


async def test_game_session_has_restricted_scope_and_parent_revocation(app, client):
    await seed(app)
    parent = await login(client)
    response = await client.post("/v1/auth/game-sessions", headers=auth(parent))
    assert response.status_code == 200
    game = response.json()
    assert game["session_kind"] == "game"
    assert (await client.get("/v1/me", headers=auth(game))).status_code == 200
    for path in ("/v1/auth/game-sessions", "/v1/auth/logout-all"):
        assert (await client.post(path, headers=auth(game))).status_code == 403
    assert (
        await client.patch(
            "/v1/me/nickname",
            headers=auth(game),
            json={
                "nickname": "Changed",
            },
        )
    ).status_code == 403
    assert (await client.post("/v1/auth/logout", headers=auth(parent))).status_code == 204
    assert (await client.get("/v1/me", headers=auth(game))).status_code == 401
    assert (
        await client.post(
            "/v1/auth/refresh",
            json={
                "refresh_token": game["refresh_token"],
            },
        )
    ).status_code == 401


async def test_duplicate_account_creation_is_atomic(app):
    async def create():
        try:
            await seed(app, login="duplicate")
            return 201
        except HTTPException as exc:
            return exc.status_code

    assert sorted(await asyncio.gather(create(), create())) == [201, 409]


async def test_wrong_and_missing_accounts_have_same_error(app, client):
    await seed(app)
    responses = [
        await client.post(
            "/v1/auth/login",
            json={
                "login_id": name,
                "password": "wrong-test-password",
            },
        )
        for name in ("tester", "absent")
    ]
    assert [r.status_code for r in responses] == [401, 401]
    assert responses[0].json() == responses[1].json()


async def test_account_rate_limit_persists_in_db(app, client):
    app.state.settings.login_account_limit = 2
    for _ in range(2):
        assert (
            await client.post(
                "/v1/auth/login",
                json={
                    "login_id": "missing",
                    "password": "password",
                },
            )
        ).status_code == 401
    limited = await client.post(
        "/v1/auth/login",
        json={
            "login_id": "missing",
            "password": "password",
        },
        headers={"X-Forwarded-For": "8.8.8.8"},
    )
    assert limited.status_code == 429
    assert int(limited.headers["retry-after"]) > 0


async def test_expired_access_refresh_and_disabled_account(app, client):
    await seed(app)
    tokens = await login(client)
    async with app.state.sessions() as db:
        session = await db.scalar(select(AuthSession))
        session.access_expires_at = now() - timedelta(seconds=1)
        await db.commit()
    assert (await client.get("/v1/me", headers=auth(tokens))).status_code == 401
    rotated = await client.post("/v1/auth/refresh", json={"refresh_token": tokens["refresh_token"]})
    assert rotated.status_code == 200
    async with app.state.sessions() as db:
        account = await db.scalar(select(Account))
        account.status = "disabled"
        await db.commit()
    assert (await client.get("/v1/me", headers=auth(rotated.json()))).status_code == 401
    assert (
        await client.post(
            "/v1/auth/login",
            json={
                "login_id": "tester",
                "password": PASSWORD,
            },
        )
    ).status_code == 401


async def test_validation_does_not_echo_password_and_no_signup_route(client):
    secret = "z" * 129
    result = await client.post("/v1/auth/login", json={"login_id": "tester", "password": secret})
    assert result.status_code == 422
    assert secret not in result.text
    assert (await client.post("/v1/auth/register", json={})).status_code == 404
    assert (await client.get("/v1/me")).status_code == 401


async def test_external_clients_and_hosts_rejected(app, client):
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app, client=("192.0.2.1", 1)),
        base_url="http://127.0.0.1:8766",
    ) as external:
        assert (await external.get("/health/live")).status_code == 403
    assert (await client.get("/health/live", headers={"host": "evil.example"})).status_code == 400


@pytest.mark.parametrize("name", ["<b>bad</b>", "a\u202eb", "x", "x" * 17])
def test_invalid_nicknames(name):
    with pytest.raises(ValidationError):
        AccountCreate(login_id="tester", nickname=name, password=PASSWORD)


def test_production_and_nonlocal_binding_fail_closed(test_url):
    with pytest.raises(ValidationError):
        Settings(environment="production", database_url=test_url)
    with pytest.raises(ValidationError):
        Settings(environment="local", database_url=test_url, host="0.0.0.0")


async def test_expired_refresh_session_cannot_be_renewed(app, client):
    await seed(app)
    tokens = await login(client)
    async with app.state.sessions() as db:
        session = await db.scalar(select(AuthSession))
        session.expires_at = now() - timedelta(seconds=1)
        await db.commit()
    assert (
        await client.post(
            "/v1/auth/refresh",
            json={
                "refresh_token": tokens["refresh_token"],
            },
        )
    ).status_code == 401


async def test_parallel_authenticated_requests_do_not_exhaust_pool(app, client):
    await seed(app)
    tokens = await login(client)
    responses = await asyncio.gather(
        *[client.post("/v1/auth/game-sessions", headers=auth(tokens)) for _ in range(7)]
    )
    assert all(response.status_code == 200 for response in responses)


async def test_malformed_unicode_is_rejected_without_secret_echo(client):
    response = await client.post(
        "/v1/auth/login",
        content=b'{"login_id":"tester","password":"\\ud800"}',
        headers={"Content-Type": "application/json"},
    )
    assert response.status_code == 422
    assert "input" not in response.text


@pytest.mark.parametrize("size,accepted", [(7, False), (8, True), (15, True), (16, False)])
@pytest.mark.parametrize("character", ["a", "가", "🎮"])
def test_new_password_length_in_unicode_codepoints(size, accepted, character):
    password = character * size
    if accepted:
        assert AccountCreate(login_id="tester", nickname="Tester", password=password)
        assert PasswordChange(current_password="legacy" * 5, new_password=password)
    else:
        with pytest.raises(ValidationError):
            AccountCreate(login_id="tester", nickname="Tester", password=password)
        with pytest.raises(ValidationError):
            PasswordChange(current_password="legacy" * 5, new_password=password)


async def test_legacy_long_password_login_and_change_to_eight_characters(app, client):
    await seed(app)
    legacy = "legacy-password-over-fifteen"
    async with app.state.sessions() as db:
        account = await db.scalar(select(Account))
        account.password_hash = await app.state.passwords.hash(legacy)
        await db.commit()
    response = await client.post("/v1/auth/login", json={"login_id": "tester", "password": legacy})
    assert response.status_code == 200
    token = response.json()["access_token"]
    for new_password in ("x" * 7, "x" * 16):
        response = await client.post(
            "/v1/auth/password",
            headers={"Authorization": "Bearer " + token},
            json={"current_password": legacy, "new_password": new_password},
        )
        assert response.status_code == 422
        assert new_password not in response.text
    response = await client.post(
        "/v1/auth/password",
        headers={"Authorization": "Bearer " + token},
        json={"current_password": legacy, "new_password": "eight123"},
    )
    assert response.status_code == 204
    assert (
        await client.post("/v1/auth/login", json={"login_id": "tester", "password": "eight123"})
    ).status_code == 200
    assert (
        await client.get("/v1/me", headers={"Authorization": "Bearer " + token})
    ).status_code == 401
