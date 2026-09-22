"""Only the authenticated Gateway may issue a Discord ownership ticket."""

from datetime import timedelta
from typing import Literal

from fastapi import HTTPException
from pydantic import Field, SecretStr
from sqlalchemy import func, select, text, update
from sqlalchemy.exc import IntegrityError

from .accounts import audit, lock_account, profile, revoke_all
from .models import Account, DiscordIdentity, DiscordTicket
from .schemas import AccountCreate, Input, Nickname
from .security import digest, now, token


class SetupAction(Input):
    action: Literal["create", "link", "nickname", "password"]
    login_id: str = Field(default="", max_length=24)
    nickname: str = Field(default="", max_length=64)
    password: SecretStr = Field(default=SecretStr(""), max_length=128)
    new_password: SecretStr = Field(default=SecretStr(""), max_length=128)


async def issue_ticket(db, discord_id: int):
    # Serializes ticket issuance and mutation for one verified Discord user
    await db.execute(text("SELECT pg_advisory_xact_lock(:id)"), {"id": discord_id})
    count = await db.scalar(
        select(func.count())
        .select_from(DiscordTicket)
        .where(
            DiscordTicket.discord_id == discord_id,
            DiscordTicket.created_at > now() - timedelta(minutes=10),
        )
    )
    if count >= 5:
        raise HTTPException(429, "10분 후 다시 시도해 주세요.")
    await db.execute(
        update(DiscordTicket)
        .where(DiscordTicket.discord_id == discord_id, DiscordTicket.consumed_at.is_(None))
        .values(consumed_at=now())
    )
    raw = token()
    db.add(
        DiscordTicket(
            token_hash=digest(raw),
            discord_id=discord_id,
            created_at=now(),
            expires_at=now() + timedelta(minutes=5),
        )
    )
    await db.commit()
    return raw


async def ticket(db, raw, locked=False):
    query = select(DiscordTicket).where(DiscordTicket.token_hash == digest(raw))
    if locked:
        query = query.with_for_update().execution_options(populate_existing=True)
    item = await db.scalar(query)
    if item is None or item.consumed_at or item.expires_at <= now() or item.attempts >= 5:
        raise HTTPException(
            401, "링크가 만료되었거나 사용되었습니다. Discord에서 /계정을 다시 실행하세요."
        )
    return item


async def linked_profile(db, discord_id):
    identity = await db.get(DiscordIdentity, discord_id)
    if not identity:
        return None
    account = await db.get(Account, identity.account_id)
    return profile(account)


async def apply_action(db, passwords, raw, data: SetupAction):
    proof = await ticket(db, raw)
    discord_id = proof.discord_id
    await db.rollback()
    await db.execute(text("SELECT pg_advisory_xact_lock(:id)"), {"id": discord_id})
    proof = await ticket(db, raw, locked=True)
    proof.attempts += 1
    # Persist failed password attempts while keeping successful changes atomic
    await db.commit()
    await db.execute(text("SELECT pg_advisory_xact_lock(:id)"), {"id": discord_id})
    proof = await db.scalar(
        select(DiscordTicket)
        .where(DiscordTicket.token_hash == digest(raw))
        .with_for_update()
        .execution_options(populate_existing=True)
    )
    if proof.consumed_at or proof.expires_at <= now():
        raise HTTPException(401, "링크를 다시 발급해 주세요.")
    identity = await db.get(DiscordIdentity, discord_id)
    if data.action in {"create", "link"}:
        if identity:
            raise HTTPException(409, "이미 연결된 계정이 있습니다.")
        if data.action == "create":
            valid = AccountCreate(
                login_id=data.login_id, nickname=data.nickname, password=data.password
            )
            account = Account(
                login_id=valid.login_id,
                nickname=valid.nickname,
                password_hash=await passwords.hash(valid.password.get_secret_value()),
                created_at=now(),
                updated_at=now(),
            )
            db.add(account)
        else:
            account = await db.scalar(
                select(Account)
                .where(Account.login_id == data.login_id.strip().lower())
                .with_for_update()
            )
            valid_password = await passwords.verify(
                account.password_hash if account else passwords.dummy,
                data.password.get_secret_value(),
            )
            if not account or not valid_password or account.status != "active":
                raise HTTPException(400, "계정 정보가 일치하지 않습니다.")
        try:
            await db.flush()
            db.add(DiscordIdentity(discord_id=discord_id, account_id=account.id, created_at=now()))
            await db.flush()
        except IntegrityError:
            await db.rollback()
            raise HTTPException(409, "아이디가 사용 중이거나 이미 연결된 계정입니다.") from None
        audit(db, account.id, "discord_account_" + data.action)
    else:
        if not identity:
            raise HTTPException(404, "계정을 먼저 생성하거나 연결하세요.")
        account = await lock_account(db, identity.account_id)
        if account.status != "active":
            raise HTTPException(403, "사용할 수 없는 계정입니다.")
        if data.action == "nickname":
            account.nickname = Nickname(nickname=data.nickname).nickname
            account.updated_at = now()
            audit(db, account.id, "discord_nickname_changed")
        else:
            if not await passwords.verify(account.password_hash, data.password.get_secret_value()):
                raise HTTPException(400, "현재 비밀번호가 일치하지 않습니다.")
            valid = AccountCreate(
                login_id=account.login_id, nickname=account.nickname, password=data.new_password
            )
            account.password_hash = await passwords.hash(valid.password.get_secret_value())
            await revoke_all(db, account, "discord_password_changed")
    proof.consumed_at = now()
    await db.commit()
    return profile(account)
