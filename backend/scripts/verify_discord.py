"""Read-only check of the configured Discord bot; never print its token."""

import asyncio

import httpx
from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Credentials(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="GEUMEUM_DISCORD_", env_file=".env", extra="ignore", hide_input_in_errors=True
    )
    application_id: int
    bot_token: SecretStr


async def main():
    try:
        credentials = Credentials()
    except ValueError:
        raise SystemExit("Discord application ID or bot token is missing/invalid in .env") from None
    async with httpx.AsyncClient(
        base_url="https://discord.com/api/v10/",
        headers={"Authorization": "Bot " + credentials.bot_token.get_secret_value()},
        timeout=15,
        follow_redirects=False,
        trust_env=False,
    ) as client:
        try:
            user = await client.get("users/@me")
            application = await client.get("oauth2/applications/@me")
        except httpx.HTTPError:
            raise SystemExit("Discord connection failed; token was not printed") from None
        if user.status_code != 200 or application.status_code != 200:
            raise SystemExit(
                f"Discord verification failed: HTTP {user.status_code}/{application.status_code}"
            )
        identity, app = user.json(), application.json()
        if not identity.get("bot") or str(app.get("id")) != str(credentials.application_id):
            raise SystemExit("Discord bot/application identity does not match local configuration")
        print(f"Discord bot verified: application_id={app['id']}, bot_id={identity['id']}")
        print("Read-only verification completed; no Discord message was sent")


if __name__ == "__main__":
    asyncio.run(main())
