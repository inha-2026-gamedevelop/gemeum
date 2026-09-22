import ipaddress
from typing import Literal
from urllib.parse import urlsplit

from pydantic import Field, SecretStr, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="GEUMEUM_",
        env_file=".env",
        extra="ignore",
        hide_input_in_errors=True,
    )

    environment: Literal["local", "test", "production"] = "local"
    database_url: SecretStr
    test_database_url: SecretStr | None = None
    public_origin: str = ""
    host: str = "127.0.0.1"
    port: int = Field(default=8766, ge=1024, le=65535)
    access_seconds: int = Field(default=900, ge=30, le=3600)
    session_seconds: int = Field(default=30 * 86400, ge=3600, le=30 * 86400)
    login_window_seconds: int = Field(default=300, ge=10, le=3600)
    login_account_limit: int = Field(default=10, ge=1)
    login_ip_limit: int = Field(default=60, ge=1)
    hash_concurrency: int = Field(default=2, ge=1, le=8)
    game_executable: str = ""
    game_hosting: Literal["listen", "dedicated"] = "listen"
    game_build_id: str = ""
    game_port_start: int = Field(default=18000, ge=1024, le=65000)
    game_max_rooms: int = Field(default=3, ge=1, le=10000)
    relay_enabled: bool = False
    relay_public_address: str = "127.0.0.1"
    relay_bind_address: str = "127.0.0.1"
    relay_port: int = Field(default=18001, ge=1024, le=65535)
    relay_bytes_per_second: int = Field(default=8_000_000, ge=100_000, le=100_000_000)

    @model_validator(mode="after")
    def validate_environment(self) -> "Settings":
        if self.relay_enabled:
            address = ipaddress.IPv4Address(self.relay_public_address)
            ipaddress.IPv4Address(self.relay_bind_address)
            if address.is_unspecified or address.is_multicast or self.game_hosting != "listen":
                raise ValueError("Relay requires a usable IPv4 address and listen hosting")
            if self.environment == "production" and not address.is_global:
                raise ValueError("Production relay requires a public IPv4 address")
            if self.environment != "production" and self.relay_bind_address != "127.0.0.1":
                raise ValueError("Local relay must bind to loopback")
        parsed = urlsplit(self.database_url.get_secret_value())
        if parsed.scheme != "postgresql+psycopg":
            raise ValueError("PostgreSQL psycopg URL is required")
        if self.environment in {"local", "test"}:
            if self.host not in {"127.0.0.1", "localhost", "::1"}:
                raise ValueError("Local service must bind to loopback")
            if parsed.hostname not in {"127.0.0.1", "localhost", "::1"}:
                raise ValueError("Local service must use a local PostgreSQL server")
        if self.environment == "production":
            origin = urlsplit(self.public_origin)
            if (
                origin.scheme != "https"
                or not origin.hostname
                or origin.username
                or origin.password
                or origin.query
                or origin.fragment
                or origin.path not in {"", "/"}
                or origin.hostname in {"localhost", "127.0.0.1", "::1"}
            ):
                raise ValueError("Production requires a public HTTPS origin")
            if self.host != "127.0.0.1" or self.game_hosting != "listen":
                raise ValueError("Production requires a loopback reverse proxy and listen hosting")
            if not self.game_build_id:
                raise ValueError("Production requires the supported game build ID")
        return self

    @property
    def account_origin(self) -> str:
        return (
            self.public_origin.rstrip("/")
            if self.environment == "production"
            else f"http://127.0.0.1:{self.port}"
        )
