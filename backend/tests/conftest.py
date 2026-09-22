import asyncio
import os
import subprocess
import sys
from pathlib import Path
from urllib.parse import urlsplit

import httpx
import pytest
from sqlalchemy import text

from geumeum_backend.api import create_app
from geumeum_backend.config import Settings

if sys.platform == "win32":
    asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture(scope="session")
def test_url():
    settings = Settings()
    if settings.test_database_url is None:
        pytest.fail("GEUMEUM_TEST_DATABASE_URL must name a separate local test database")
    url = settings.test_database_url.get_secret_value()
    parsed = urlsplit(url)
    if parsed.hostname not in {"localhost", "127.0.0.1"} or parsed.path != "/geumeum_test":
        pytest.fail("Tests only allow the loopback geumeum_test database")
    if url == settings.database_url.get_secret_value():
        pytest.fail("Test DB must not be the application DB")
    env = dict(os.environ, GEUMEUM_DATABASE_URL=url, GEUMEUM_ENVIRONMENT="test")
    subprocess.run(
        [sys.executable, "-m", "alembic", "upgrade", "head"],
        cwd=ROOT,
        env=env,
        check=True,
    )
    return url


@pytest.fixture
async def app(test_url):
    app = create_app(Settings(environment="test", database_url=test_url, game_hosting="dedicated"))
    async with app.state.engine.begin() as conn:
        await conn.execute(
            text(
                "TRUNCATE accounts, auth_sessions, refresh_tokens, rate_buckets, "
                "audit_events, release_notices, notification_outbox, discord_tickets, "
                "discord_identities RESTART IDENTITY CASCADE"
            )
        )
    async with app.router.lifespan_context(app):
        yield app


@pytest.fixture
async def client(app):
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app, client=("127.0.0.1", 12345)),
        base_url="http://127.0.0.1:8766",
    ) as client:
        yield client
