"""Bind room hosting credentials to the owner's game session."""

import sqlalchemy as sa
from alembic import op

revision = "a83194bc3201"
down_revision = "f21790b6a301"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column(
        "rooms", sa.Column("hosting", sa.String(16), nullable=False, server_default="dedicated")
    )
    op.add_column("rooms", sa.Column("address", sa.String(45), nullable=True))
    op.add_column(
        "rooms",
        sa.Column("host_session_id", sa.Uuid(), sa.ForeignKey("auth_sessions.id"), nullable=True),
    )


def downgrade():
    op.drop_column("rooms", "host_session_id")
    op.drop_column("rooms", "address")
    op.drop_column("rooms", "hosting")
