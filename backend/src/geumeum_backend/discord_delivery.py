"""Durable delivery: ambiguous outcomes require review, never blind resending."""

from datetime import timedelta

import httpx
from sqlalchemy import or_, select, update

from .models import NotificationOutbox, ReleaseNotice
from .security import now


async def deliver_one(sessions, http, channels):
    async with sessions() as db:
        await db.execute(
            update(NotificationOutbox)
            .where(
                NotificationOutbox.state == "sending",
                NotificationOutbox.attempted_at < now() - timedelta(minutes=5),
            )
            .values(state="needs_review")
        )
        row = (
            await db.execute(
                select(NotificationOutbox, ReleaseNotice)
                .join(ReleaseNotice, NotificationOutbox.release_id == ReleaseNotice.id)
                .where(
                    NotificationOutbox.state == "pending",
                    ReleaseNotice.channel.in_(channels),
                    or_(
                        NotificationOutbox.retry_at.is_(None), NotificationOutbox.retry_at <= now()
                    ),
                )
                .order_by(NotificationOutbox.created_at)
                .with_for_update(skip_locked=True, of=NotificationOutbox)
                .limit(1)
            )
        ).first()
        if not row:
            await db.commit()
            return False
        item, notice = row
        channel_id = str(channels[notice.channel])
        item.state = "sending"
        item.attempts += 1
        item.attempted_at = now()
        item.discord_channel_id = channel_id
        item_id, attempts = item.id, item.attempts
        payload = {
            "content": f"[{notice.product}/{notice.channel}] {notice.version}\n{notice.notes}",
            "allowed_mentions": {"parse": []},
            "nonce": item.id.hex[:25],
            "enforce_nonce": True,
        }
        await db.commit()
    state, message_id, delay = "needs_review", None, 0
    try:
        response = await http.post(f"channels/{channel_id}/messages", json=payload)
        if response.status_code == 200:
            body = response.json()
            if str(body.get("channel_id")) == channel_id and str(body.get("id", "")).isdigit():
                state, message_id = "sent", str(body["id"])
        elif response.status_code == 429:
            state = "pending"
            delay = min(3600, max(5, float(response.json().get("retry_after", 60))))
        elif response.status_code in {400, 401, 403, 404, 405, 413}:
            state = "failed"
    except (httpx.HTTPError, ValueError, TypeError):
        pass
    if state == "pending" and attempts >= 10:
        state = "failed"
    async with sessions() as db:
        await db.execute(
            update(NotificationOutbox)
            .where(NotificationOutbox.id == item_id, NotificationOutbox.state == "sending")
            .values(
                state=state,
                discord_message_id=message_id,
                retry_at=now() + timedelta(seconds=delay) if delay else None,
            )
        )
        await db.commit()
    print(f"Discord delivery: outbox={item_id}, state={state}", flush=True)
    return True
