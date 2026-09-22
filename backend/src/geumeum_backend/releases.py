"""Local release notices. Preview does not send or mark Discord delivery complete."""

from typing import Literal

from pydantic import Field
from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert

from .models import NotificationOutbox, ReleaseNotice
from .schemas import Input
from .security import now


class NewNotice(Input):
    product: Literal["game", "launcher"]
    channel: Literal["internal", "stable"]
    version: str = Field(pattern=r"^[A-Za-z0-9._-]{1,96}$")
    notes: str = Field(min_length=1, max_length=1800)


async def record_notice(db, notice):
    release_id = await db.scalar(
        insert(ReleaseNotice)
        .values(**notice.model_dump(), created_at=now())
        .on_conflict_do_nothing(index_elements=["product", "channel", "version"])
        .returning(ReleaseNotice.id)
    )
    if release_id is None:
        existing = await db.scalar(
            select(ReleaseNotice).where(
                ReleaseNotice.product == notice.product,
                ReleaseNotice.channel == notice.channel,
                ReleaseNotice.version == notice.version,
            )
        )
        if existing.notes != notice.notes:
            raise ValueError("This release already has different notes; use a new version")
        return False
    db.add(NotificationOutbox(release_id=release_id, created_at=now()))
    await db.flush()
    return True


async def preview(db):
    rows = await db.scalars(
        select(ReleaseNotice)
        .join(NotificationOutbox)
        .where(NotificationOutbox.state == "pending")
        .order_by(ReleaseNotice.created_at)
        .limit(50)
    )
    return [
        {
            "product": r.product,
            "channel": r.channel,
            "version": r.version,
            "content": f"[{r.product}/{r.channel}] {r.version}\n{r.notes}",
            "allowed_mentions": {"parse": []},
        }
        for r in rows
    ]
