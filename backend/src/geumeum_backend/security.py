import hashlib
import secrets
from datetime import UTC, datetime

import anyio
from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError


def now() -> datetime:
    return datetime.now(UTC)


def digest(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def token() -> str:
    return secrets.token_urlsafe(32)


class Passwords:
    def __init__(self, concurrency: int = 2):
        self.hasher = PasswordHasher(time_cost=3, memory_cost=65536, parallelism=1)
        self.limiter = anyio.CapacityLimiter(concurrency)
        # Unknown-user login still performs the same expensive verification
        self.dummy = self.hasher.hash(token())

    async def hash(self, value: str) -> str:
        return await anyio.to_thread.run_sync(self.hasher.hash, value, limiter=self.limiter)

    async def verify(self, hashed: str, value: str) -> bool:
        def work() -> bool:
            try:
                return self.hasher.verify(hashed, value)
            except (VerificationError, InvalidHashError):
                return False

        return await anyio.to_thread.run_sync(work, limiter=self.limiter)
