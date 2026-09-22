"""Discord ownership, expiring setup tickets, and delivery state."""

import sqlalchemy as sa
from alembic import op

revision = "d912f84d0c32"
down_revision = "a4d67fc7b729"
branch_labels = None
depends_on = None


def upgrade():
    op.create_table(
        "discord_identities",
        sa.Column("discord_id", sa.BigInteger(), primary_key=True),
        sa.Column(
            "account_id", sa.BigInteger(), sa.ForeignKey("accounts.id"), unique=True, nullable=False
        ),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_table(
        "discord_tickets",
        sa.Column("token_hash", sa.String(64), primary_key=True),
        sa.Column("discord_id", sa.BigInteger(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("consumed_at", sa.DateTime(timezone=True)),
        sa.Column("attempts", sa.Integer(), nullable=False),
    )
    op.create_index("ix_discord_tickets_discord_id", "discord_tickets", ["discord_id"])
    op.add_column(
        "notification_outbox",
        sa.Column("attempts", sa.Integer(), server_default="0", nullable=False),
    )
    for name in ("attempted_at", "retry_at"):
        op.add_column("notification_outbox", sa.Column(name, sa.DateTime(timezone=True)))
    for name in ("discord_message_id", "discord_channel_id"):
        op.add_column("notification_outbox", sa.Column(name, sa.String(24)))


def downgrade():
    for name in (
        "attempts",
        "attempted_at",
        "retry_at",
        "discord_message_id",
        "discord_channel_id",
    ):
        op.drop_column("notification_outbox", name)
    op.drop_table("discord_tickets")
    op.drop_table("discord_identities")
