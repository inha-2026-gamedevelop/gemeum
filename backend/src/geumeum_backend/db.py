from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine

from .config import Settings


def database(settings: Settings):
    engine = create_async_engine(
        settings.database_url.get_secret_value(),
        pool_size=5,
        max_overflow=2,
        pool_timeout=5,
        pool_pre_ping=True,
        connect_args={"connect_timeout": 5, "options": "-c statement_timeout=10000"},
    )
    return engine, async_sessionmaker(engine, expire_on_commit=False)
