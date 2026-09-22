import asyncio
import logging
from contextlib import asynccontextmanager, suppress
from datetime import UTC, datetime
from typing import Annotated
from urllib.parse import urlsplit

from fastapi import Depends, FastAPI, HTTPException, Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy import select, text
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.ext.asyncio import AsyncSession
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .accounts import audit, issue_tokens, lock_account, profile, revoke_all, session_valid
from .config import Settings
from .db import database
from .models import Account, AuthSession, RateBucket, RefreshToken
from .schemas import Login, Nickname, PasswordChange, Profile, Refresh, Tokens
from .security import Passwords, digest, now

logger = logging.getLogger("geumeum.api")
bearer = HTTPBearer(auto_error=False)


async def get_db(request: Request):
    async with request.app.state.sessions() as db:
        yield db


DB = Annotated[AsyncSession, Depends(get_db)]


async def throttle(request: Request, entries: list[tuple[str, int]]):
    settings = request.app.state.settings
    seconds = settings.login_window_seconds
    timestamp = now().timestamp()
    window = datetime.fromtimestamp(int(timestamp // seconds) * seconds, UTC)
    denied = False
    async with request.app.state.sessions() as db:
        # Counters survive process restarts and are shared by all API workers
        for key, limit in sorted(entries):
            stmt = insert(RateBucket).values(key=digest(key), starts_at=window, hits=1)
            hits = await db.scalar(
                stmt.on_conflict_do_update(
                    index_elements=[RateBucket.key, RateBucket.starts_at],
                    set_={"hits": RateBucket.hits + 1},
                ).returning(RateBucket.hits)
            )
            denied |= hits > limit
        await db.commit()
    if denied:
        raise HTTPException(
            429,
            "Too many attempts; retry later",
            headers={
                "Retry-After": str(max(1, seconds - int(timestamp - window.timestamp()))),
            },
        )


def peer(request: Request) -> str:
    # Do not trust user-provided X-Forwarded-For in this local milestone
    return request.client.host if request.client else "unknown"


async def authenticate(
    request: Request,
    db: DB,
    credentials: Annotated[HTTPAuthorizationCredentials | None, Depends(bearer)],
) -> tuple[Account, AuthSession]:
    if credentials is None or not 40 <= len(credentials.credentials) <= 128:
        raise HTTPException(401, "Authentication required", headers={"WWW-Authenticate": "Bearer"})
    session = await db.scalar(
        select(AuthSession).where(
            AuthSession.access_hash == digest(credentials.credentials),
        )
    )
    if session is None:
        raise HTTPException(401, "Session is invalid or expired")
    account = await db.get(Account, session.account_id)
    if not await session_valid(db, account, session) or session.access_expires_at <= now():
        raise HTTPException(401, "Session is invalid or expired")
    # Release the read transaction before a route acquires a rate-limit connection
    # expire_on_commit=False preserves these snapshots; mutations recheck under lock
    await db.commit()
    return account, session


Auth = Annotated[tuple[Account, AuthSession], Depends(authenticate)]


def launcher_only(auth: tuple[Account, AuthSession]):
    if auth[1].kind != "launcher":
        raise HTTPException(403, "Launcher session required")


async def recheck_locked(db: AsyncSession, auth: tuple[Account, AuthSession]):
    account = await lock_account(db, auth[0].id)
    await db.refresh(auth[1])
    if not await session_valid(db, account, auth[1]) or auth[1].access_expires_at <= now():
        raise HTTPException(401, "Session is invalid or expired")
    return account


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or Settings()
    engine, sessions = database(settings)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        # Fail closed if migrations/DB are unavailable; never create schema during startup
        async with engine.connect() as conn:
            await conn.execute(text("SELECT version_num FROM alembic_version"))
        from .rooms import expire_listen_rooms

        leases = asyncio.create_task(expire_listen_rooms(app))
        try:
            if settings.relay_enabled:
                await app.state.relay.start()
            yield
        finally:
            app.state.relay.close()
            leases.cancel()
            with suppress(asyncio.CancelledError):
                await leases
            await engine.dispose()

    app = FastAPI(
        title="Geumeum Account API",
        version="0.1.0",
        lifespan=lifespan,
        docs_url=None if settings.environment == "production" else "/docs",
        redoc_url=None if settings.environment == "production" else "/redoc",
    )
    app.state.settings = settings
    app.state.engine = engine
    app.state.sessions = sessions
    app.state.passwords = Passwords(settings.hash_concurrency)
    from .relay import Relay

    app.state.relay = Relay(settings)
    from .discord_web import install

    install(app)
    allowed_hosts = ["127.0.0.1", "localhost", "[::1]"]
    if settings.environment == "production":
        allowed_hosts.append(urlsplit(settings.public_origin).hostname)
    app.add_middleware(TrustedHostMiddleware, allowed_hosts=allowed_hosts)

    @app.middleware("http")
    async def local_boundary(request: Request, call_next):
        if (
            settings.environment != "production"
            and request.client
            and request.client.host not in {"127.0.0.1", "::1"}
        ):
            return JSONResponse({"detail": "Local connections only"}, status_code=403)
        if settings.environment == "production" and request.url.scheme != "https":
            if not (
                request.url.path.startswith("/health/")
                and request.client
                and request.client.host in {"127.0.0.1", "::1"}
            ):
                return JSONResponse({"detail": "HTTPS required"}, status_code=403)
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        return response

    @app.exception_handler(RequestValidationError)
    async def validation_error(_request: Request, exc: RequestValidationError):
        # Pydantic errors can contain the original plaintext password in `input`/`ctx`.
        errors = [{"loc": e["loc"], "type": e["type"]} for e in exc.errors()]
        return JSONResponse({"detail": errors}, status_code=422)

    @app.exception_handler(SQLAlchemyError)
    async def database_error(_request: Request, exc: SQLAlchemyError):
        logger.error("Database request failed (%s)", type(exc).__name__)
        return JSONResponse({"detail": "Database temporarily unavailable"}, status_code=503)

    @app.get("/health/live")
    async def live():
        return {"status": "ok", "environment": settings.environment}

    @app.get("/health/ready")
    async def ready(db: DB):
        await db.execute(text("SELECT 1 FROM alembic_version"))
        return {"status": "ready"}

    @app.post("/v1/auth/login", response_model=Tokens)
    async def login(data: Login, request: Request, db: DB):
        login_id = data.login_id.strip().lower()
        await throttle(
            request,
            [
                ("login:account:" + login_id, settings.login_account_limit),
                ("login:ip:" + peer(request), settings.login_ip_limit),
            ],
        )
        account = await db.scalar(select(Account).where(Account.login_id == login_id))
        hashed = account.password_hash if account else app.state.passwords.dummy
        # Release DB connection while waiting for an expensive password calculation
        await db.rollback()
        valid = await app.state.passwords.verify(hashed, data.password.get_secret_value())
        if not valid or account is None:
            raise HTTPException(401, "Invalid login ID or password")
        # Re-read after hashing; reset/disable concurrent with login must win
        account = await db.scalar(
            select(Account)
            .where(
                Account.login_id == login_id,
            )
            .with_for_update()
            .execution_options(populate_existing=True)
        )
        if account.status != "active" or account.password_hash != hashed:
            raise HTTPException(401, "Invalid login ID or password")
        result = await issue_tokens(db, settings, account)
        audit(db, account.id, "login")
        await db.commit()
        return result

    @app.post("/v1/auth/refresh", response_model=Tokens)
    async def refresh(data: Refresh, request: Request, db: DB):
        await throttle(request, [("refresh:ip:" + peer(request), settings.login_ip_limit)])
        hashed = digest(data.refresh_token.get_secret_value())
        existing = await db.get(RefreshToken, hashed)
        if existing is None:
            raise HTTPException(401, "Session is invalid or expired")
        session = await db.get(AuthSession, existing.session_id)
        account = await lock_account(db, session.account_id)
        # Lock order: account, session, token; refresh/logout/password change serialize by account
        await db.refresh(session)
        await db.refresh(existing, with_for_update=True)
        if not await session_valid(db, account, session):
            raise HTTPException(401, "Session is invalid or expired")
        if existing.consumed_at is not None:
            session.revoked_at = now()
            audit(db, account.id, "refresh_reuse_revoked")
            await db.commit()
            raise HTTPException(401, "Session is invalid or expired")
        existing.consumed_at = now()
        result = await issue_tokens(db, settings, account, session)
        await db.commit()
        return result

    @app.get("/v1/me", response_model=Profile)
    async def me(auth: Auth):
        return profile(auth[0])

    @app.patch("/v1/me/nickname", response_model=Profile)
    async def nickname(data: Nickname, request: Request, auth: Auth, db: DB):
        launcher_only(auth)
        await throttle(request, [(f"nickname:{auth[0].id}", 5)])
        account = await recheck_locked(db, auth)
        account.nickname = data.nickname
        account.updated_at = now()
        audit(db, account.id, "nickname_changed")
        await db.commit()
        return profile(account)

    @app.post("/v1/auth/logout", status_code=204)
    async def logout(auth: Auth, db: DB):
        await recheck_locked(db, auth)
        auth[1].revoked_at = now()
        audit(db, auth[0].id, "logout")
        await db.commit()
        return Response(status_code=204)

    @app.post("/v1/auth/logout-all", status_code=204)
    async def logout_all(auth: Auth, db: DB):
        launcher_only(auth)
        account = await recheck_locked(db, auth)
        await revoke_all(db, account, "logout_all")
        await db.commit()
        return Response(status_code=204)

    @app.post("/v1/auth/password", status_code=204)
    async def change_password(data: PasswordChange, request: Request, auth: Auth, db: DB):
        launcher_only(auth)
        await throttle(request, [(f"password:{auth[0].id}", 5)])
        account_id, old_hash = auth[0].id, auth[0].password_hash
        # Preserve identifiers before rollback expires ORM objects
        session_id = auth[1].id
        await db.rollback()
        if not await app.state.passwords.verify(old_hash, data.current_password.get_secret_value()):
            raise HTTPException(401, "Current password is incorrect")
        new_hash = await app.state.passwords.hash(data.new_password.get_secret_value())
        account = await lock_account(db, account_id)
        session = await db.get(AuthSession, session_id, populate_existing=True)
        if (
            account.password_hash != old_hash
            or not await session_valid(db, account, session)
            or session.access_expires_at <= now()
        ):
            raise HTTPException(401, "Session is invalid or expired")
        account.password_hash = new_hash
        await revoke_all(db, account, "password_changed")
        await db.commit()
        return Response(status_code=204)

    @app.post("/v1/auth/game-sessions", response_model=Tokens)
    async def game_session(request: Request, auth: Auth, db: DB):
        launcher_only(auth)
        await throttle(request, [(f"game_session:{auth[0].id}", 10)])
        account = await recheck_locked(db, auth)
        result = await issue_tokens(db, settings, account, parent=auth[1])
        audit(db, account.id, "game_session_created")
        await db.commit()
        return result

    from .rooms import install

    install(app)
    return app
