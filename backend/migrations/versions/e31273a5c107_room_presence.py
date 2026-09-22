"""Track authenticated room presence separately from reservations."""

import sqlalchemy as sa
from alembic import op

revision = "e31273a5c107"
down_revision = "d912f84d0c32"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column(
        "room_members",
        sa.Column("connected", sa.Boolean(), nullable=False, server_default=sa.false()),
    )


def downgrade():
    op.drop_column("room_members", "connected")
