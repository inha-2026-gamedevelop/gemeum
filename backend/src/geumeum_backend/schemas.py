import re
import unicodedata
from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, SecretStr, field_validator


class Input(BaseModel):
    model_config = ConfigDict(extra="forbid")

    @field_validator("*")
    @classmethod
    def valid_unicode(cls, value):
        raw = value.get_secret_value() if isinstance(value, SecretStr) else value
        if isinstance(raw, str):
            try:
                raw.encode("utf-8")
            except UnicodeEncodeError:
                raise ValueError("Invalid Unicode input") from None
        return value


class Login(Input):
    login_id: str = Field(min_length=1, max_length=128)
    password: SecretStr = Field(min_length=1, max_length=128)


class AccountCreate(Input):
    login_id: str
    nickname: str
    password: SecretStr = Field(min_length=8, max_length=15)

    @field_validator("login_id")
    @classmethod
    def login(cls, value: str) -> str:
        value = value.strip().lower()
        if not re.fullmatch(r"[a-z0-9_]{4,24}", value):
            raise ValueError("Login ID must be 4-24 ASCII letters, digits or underscores")
        return value

    @field_validator("nickname")
    @classmethod
    def nick(cls, value: str) -> str:
        return validate_nickname(value)


def validate_nickname(value: str) -> str:
    value = unicodedata.normalize("NFC", value.strip())
    if not 2 <= len(value) <= 16:
        raise ValueError("Nickname must be 2-16 Unicode code points after NFC normalization")
    if any(unicodedata.category(c).startswith("C") or c in "<>" for c in value):
        raise ValueError("Nickname contains prohibited formatting or control characters")
    return value


class Nickname(Input):
    nickname: str

    @field_validator("nickname")
    @classmethod
    def nick(cls, value: str) -> str:
        return validate_nickname(value)


class Refresh(Input):
    refresh_token: SecretStr = Field(min_length=40, max_length=128)


class PasswordChange(Input):
    current_password: SecretStr = Field(min_length=1, max_length=128)
    new_password: SecretStr = Field(min_length=8, max_length=15)


class Profile(BaseModel):
    account_id: str
    login_id: str
    nickname: str
    status: str


class Tokens(BaseModel):
    access_token: str
    refresh_token: str
    token_type: Literal["Bearer"] = "Bearer"
    expires_in: int
    session_expires_at: datetime
    session_kind: str
    account: Profile
