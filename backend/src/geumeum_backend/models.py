import uuid
from datetime import datetime

from sqlalchemy import (
    JSON,
    BigInteger,
    DateTime,
    ForeignKey,
    Identity,
    Index,
    String,
    Text,
    UniqueConstraint,
    Uuid,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class ReleaseNotice(Base):
    __tablename__ = "release_notices"
    __table_args__ = (UniqueConstraint("product", "channel", "version"),)

    id: Mapped[uuid.UUID] = mapped_column(Uuid, primary_key=True, default=uuid.uuid4)
    product: Mapped[str] = mapped_column(String(16))
    channel: Mapped[str] = mapped_column(String(16))
    version: Mapped[str] = mapped_column(String(96))
    notes: Mapped[str] = mapped_column(Text)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class NotificationOutbox(Base):
    __tablename__ = "notification_outbox"

    id: Mapped[uuid.UUID] = mapped_column(Uuid, primary_key=True, default=uuid.uuid4)
    release_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("release_notices.id"), unique=True)
    state: Mapped[str] = mapped_column(String(16), default="pending")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    attempts: Mapped[int] = mapped_column(default=0)
    attempted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    retry_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    discord_message_id: Mapped[str | None] = mapped_column(String(24))
    discord_channel_id: Mapped[str | None] = mapped_column(String(24))


class DiscordIdentity(Base):
    __tablename__ = "discord_identities"
    discord_id: Mapped[int] = mapped_column(BigInteger, primary_key=True)
    account_id: Mapped[int] = mapped_column(ForeignKey("accounts.id"), unique=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class DiscordTicket(Base):
    __tablename__ = "discord_tickets"
    token_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    discord_id: Mapped[int] = mapped_column(BigInteger, index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    consumed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    attempts: Mapped[int] = mapped_column(default=0)


class Account(Base):
    __tablename__ = "accounts"

    id: Mapped[int] = mapped_column(BigInteger, Identity(), primary_key=True)
    login_id: Mapped[str] = mapped_column(String(24), unique=True)
    nickname: Mapped[str] = mapped_column(String(64))
    password_hash: Mapped[str] = mapped_column(Text)
    status: Mapped[str] = mapped_column(String(16), default="active")
    security_version: Mapped[int] = mapped_column(default=1)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class AuthSession(Base):
    __tablename__ = "auth_sessions"

    id: Mapped[uuid.UUID] = mapped_column(Uuid, primary_key=True, default=uuid.uuid4)
    account_id: Mapped[int] = mapped_column(ForeignKey("accounts.id"), index=True)
    kind: Mapped[str] = mapped_column(String(16), default="launcher")
    parent_id: Mapped[uuid.UUID | None] = mapped_column(ForeignKey("auth_sessions.id"))
    access_hash: Mapped[str] = mapped_column(String(64), unique=True)
    access_expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    security_version: Mapped[int]
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class RefreshToken(Base):
    __tablename__ = "refresh_tokens"

    token_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    session_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("auth_sessions.id"), index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    consumed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class RateBucket(Base):
    __tablename__ = "rate_buckets"

    key: Mapped[str] = mapped_column(String(64), primary_key=True)
    starts_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), primary_key=True)
    hits: Mapped[int]


class AuditEvent(Base):
    __tablename__ = "audit_events"

    id: Mapped[int] = mapped_column(BigInteger, Identity(), primary_key=True)
    account_id: Mapped[int | None] = mapped_column(ForeignKey("accounts.id"))
    action: Mapped[str] = mapped_column(String(48))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


Index("ix_auth_sessions_expires_at", AuthSession.expires_at)
Index("ix_rate_buckets_starts_at", RateBucket.starts_at)


class Room(Base):
    __tablename__ = "rooms"
    id: Mapped[uuid.UUID] = mapped_column(Uuid, primary_key=True, default=uuid.uuid4)
    owner_id: Mapped[int] = mapped_column(ForeignKey("accounts.id"), index=True)
    name: Mapped[str] = mapped_column(String(64))
    build_id: Mapped[str] = mapped_column(String(96))
    state: Mapped[str] = mapped_column(String(16), default="queued")
    port: Mapped[int | None]
    server_hash: Mapped[str | None] = mapped_column(String(64))
    hosting: Mapped[str] = mapped_column(String(16), default="dedicated")
    address: Mapped[str | None] = mapped_column(String(45))
    host_session_id: Mapped[uuid.UUID | None] = mapped_column(ForeignKey("auth_sessions.id"))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    heartbeat_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    fill_with_bots: Mapped[bool] = mapped_column(default=False)
    launch_members: Mapped[list | None] = mapped_column(JSON)
    launch_deadline: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class RoomMember(Base):
    __tablename__ = "room_members"
    room_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("rooms.id"), primary_key=True)
    account_id: Mapped[int] = mapped_column(ForeignKey("accounts.id"), primary_key=True)
    session_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("auth_sessions.id"))
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    connected: Mapped[bool] = mapped_column(default=False)
    launcher_session_id: Mapped[uuid.UUID | None] = mapped_column(ForeignKey("auth_sessions.id"))
    preference: Mapped[int | None]
    ready: Mapped[bool] = mapped_column(default=False)


class JoinTicket(Base):
    __tablename__ = "join_tickets"
    token_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    room_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("rooms.id"), index=True)
    account_id: Mapped[int] = mapped_column(ForeignKey("accounts.id"))
    session_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("auth_sessions.id"))
    challenge: Mapped[str] = mapped_column(String(64))
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    consumed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
