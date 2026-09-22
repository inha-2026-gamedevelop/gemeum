import pytest
from sqlalchemy import func, select

from geumeum_backend.models import NotificationOutbox, ReleaseNotice
from geumeum_backend.releases import NewNotice, preview, record_notice


async def test_notice_retry_and_preview_do_not_duplicate_or_consume(app):
    notice = NewNotice(
        product="game", channel="internal", version="test-1", notes="Update @everyone"
    )
    async with app.state.sessions() as db:
        assert await record_notice(db, notice)
        await db.commit()
        assert not await record_notice(db, notice)
        first = await preview(db)
        assert first == await preview(db)
        assert first[0]["allowed_mentions"] == {"parse": []}
        assert await db.scalar(select(func.count()).select_from(NotificationOutbox)) == 1
        with pytest.raises(ValueError):
            await record_notice(db, notice.model_copy(update={"notes": "Changed"}))


async def test_rollback_removes_release_and_notification_together(app):
    async with app.state.sessions() as db:
        await record_notice(
            db, NewNotice(product="launcher", channel="stable", version="1.0.0", notes="Test")
        )
        await db.rollback()
        assert await db.scalar(select(func.count()).select_from(ReleaseNotice)) == 0
        assert await db.scalar(select(func.count()).select_from(NotificationOutbox)) == 0
