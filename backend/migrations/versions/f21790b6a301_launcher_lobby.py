"""Keep party preparation in the launcher, then freeze the launch roster."""

import sqlalchemy as sa
from alembic import op

revision = "f21790b6a301"
down_revision = "e31273a5c107"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column(
        "rooms",
        sa.Column("fill_with_bots", sa.Boolean(), nullable=False, server_default=sa.false()),
    )
    op.add_column("rooms", sa.Column("launch_members", sa.JSON(), nullable=True))
    op.add_column("rooms", sa.Column("launch_deadline", sa.DateTime(timezone=True), nullable=True))
    op.add_column(
        "room_members",
        sa.Column(
            "launcher_session_id", sa.Uuid(), sa.ForeignKey("auth_sessions.id"), nullable=True
        ),
    )
    op.add_column("room_members", sa.Column("preference", sa.Integer(), nullable=True))
    op.add_column(
        "room_members", sa.Column("ready", sa.Boolean(), nullable=False, server_default=sa.false())
    )


def downgrade():
    op.drop_column("room_members", "ready")
    op.drop_column("room_members", "preference")
    op.drop_column("room_members", "launcher_session_id")
    op.drop_column("rooms", "launch_members")
    op.drop_column("rooms", "launch_deadline")
    op.drop_column("rooms", "fill_with_bots")
