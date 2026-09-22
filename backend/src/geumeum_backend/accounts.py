from datetime import timedelta

from fastapi import HTTPException
from sqlalchemy import select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from .config import Settings
from .models import Account, AuditEvent, AuthSession, RefreshToken
from .schemas import AccountCreate, Profile, Tokens
from .security import Passwords, digest, now, token


def profile(account: Account) -> Profile:
    return Profile(
        account_id=str(account.id),
        login_id=account.login_id,
        nickname=account.nickname,
        status=account.status,
    )


def audit(db: AsyncSession, account_id: int, action: str):
    db.add(AuditEvent(account_id=account_id, action=action, created_at=now()))


async def create_account(db: AsyncSession, passwords: Passwords, data: AccountCreate) -> Account:
    timestamp = now()
    account = Account(
        login_id=data.login_id,
        nickname=data.nickname,
        password_hash=await passwords.hash(data.password.get_secret_value()),
        created_at=timestamp,
        updated_at=timestamp,
    )
    db.add(account)
    try:
        await db.flush()
        audit(db, account.id, "local_account_created")
        await db.commit()
    except IntegrityError:
        await db.rollback()
        raise HTTPException(409, "Login ID is already in use") from None
    return account


async def issue_tokens(
    db: AsyncSession,
    settings: Settings,
    account: Account,
    session: AuthSession | None = None,
    parent: AuthSession | None = None,
) -> Tokens:
    timestamp = now()
    access, refresh = token(), token()
    if session is None:
        session = AuthSession(
            account_id=account.id,
            access_hash=digest(access),
            security_version=account.security_version,
            kind="game" if parent else "launcher",
            parent_id=parent.id if parent else None,
            created_at=timestamp,
            expires_at=min(parent.expires_at, timestamp + timedelta(days=1))
            if parent
            else timestamp + timedelta(seconds=settings.session_seconds),
        )
        db.add(session)
    session.access_hash = digest(access)
    session.access_expires_at = min(
        timestamp + timedelta(seconds=settings.access_seconds),
        session.expires_at,
    )
    await db.flush()
    db.add(
        RefreshToken(
            token_hash=digest(refresh),
            session_id=session.id,
            created_at=timestamp,
        )
    )
    return Tokens(
        access_token=access,
        refresh_token=refresh,
        expires_in=max(0, int((session.access_expires_at - timestamp).total_seconds())),
        session_expires_at=session.expires_at,
        session_kind=session.kind,
        account=profile(account),
    )


async def session_valid(db: AsyncSession, account: Account, session: AuthSession) -> bool:
    if account.status != "active" or account.security_version != session.security_version:
        return False
    if session.revoked_at is not None or session.expires_at <= now():
        return False
    if session.parent_id:
        parent = await db.get(AuthSession, session.parent_id)
        if parent is None or parent.revoked_at is not None or parent.expires_at <= now():
            return False
    return True


async def revoke_all(db: AsyncSession, account: Account, action: str):
    account.security_version += 1
    account.updated_at = now()
    await db.execute(
        update(AuthSession)
        .where(
            AuthSession.account_id == account.id,
            AuthSession.revoked_at.is_(None),
        )
        .values(revoked_at=now())
    )
    audit(db, account.id, action)


async def lock_account(db: AsyncSession, account_id: int) -> Account:
    return (
        await db.scalars(
            select(Account)
            .where(Account.id == account_id)
            .with_for_update()
            .execution_options(populate_existing=True)
        )
    ).one()
