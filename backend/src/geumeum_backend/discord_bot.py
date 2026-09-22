import asyncio
from pathlib import Path

import discord
import httpx
from discord import app_commands
from fastapi import HTTPException
from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict

from .config import Settings
from .db import database
from .discord_accounts import issue_ticket, linked_profile
from .discord_delivery import deliver_one


class Credentials(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="GEUMEUM_DISCORD_", env_file=".env", extra="ignore", hide_input_in_errors=True
    )
    application_id: int = Field(gt=0)
    guild_id: int = Field(gt=0)
    bot_token: SecretStr
    notice_channel_id: int = Field(default=0, ge=0)
    internal_channel_id: int = Field(default=0, ge=0)


class AccountTree(app_commands.CommandTree):
    async def interaction_check(self, interaction):
        if interaction.guild_id != self.client.config.guild_id or interaction.user.bot:
            await interaction.response.send_message("공식 서버에서 사용해 주세요.", ephemeral=True)
            return False
        return True

    async def on_error(self, interaction, error):
        print(f"Discord command error: {type(error).__name__}", flush=True)
        message = "처리하지 못했습니다. 잠시 후 다시 시도해 주세요."
        if isinstance(error, app_commands.CommandInvokeError) and isinstance(
            error.original, HTTPException
        ):
            message = str(error.original.detail)
        if interaction.response.is_done():
            await interaction.followup.send(message, ephemeral=True)
        else:
            await interaction.response.send_message(message, ephemeral=True)


class GeumeumBot(discord.Client):
    def __init__(self, config, settings, sessions):
        intents = discord.Intents.none()
        intents.guilds = True
        super().__init__(intents=intents, allowed_mentions=discord.AllowedMentions.none())
        self.config, self.settings, self.sessions = config, settings, sessions
        self.tree = AccountTree(self)
        guild = discord.Object(id=config.guild_id)

        @self.tree.command(
            name="계정", description="계정 생성·연결·닉네임·비밀번호 설정", guild=guild
        )
        @app_commands.checks.cooldown(1, 10, key=lambda i: i.user.id)
        async def account(interaction: discord.Interaction):
            await interaction.response.defer(ephemeral=True)
            async with sessions() as db:
                raw = await issue_ticket(db, interaction.user.id)
            url = f"{settings.account_origin}/discord/account#{raw}"
            await interaction.followup.send(
                f"[계정 설정 열기]({url})\n5분·1회용 링크입니다. 공유하지 마세요.\n"
                "비밀번호는 위 계정 설정 페이지에 입력하세요. Discord 채팅에는 입력하지 마세요.",
                ephemeral=True,
                suppress_embeds=True,
            )

        @self.tree.command(name="내정보", description="연결된 게임 계정 확인", guild=guild)
        @app_commands.checks.cooldown(1, 5, key=lambda i: i.user.id)
        async def info(interaction: discord.Interaction):
            await interaction.response.defer(ephemeral=True)
            async with sessions() as db:
                value = await linked_profile(db, interaction.user.id)
            content = "연결된 계정이 없습니다. /계정을 실행하세요."
            if value:
                content = (
                    f"아이디: {value.login_id}\n"
                    f"닉네임: {discord.utils.escape_markdown(value.nickname)}\n"
                    f"상태: {value.status}"
                )
            await interaction.followup.send(content, ephemeral=True)

        @self.tree.command(
            name="알림", description="게임·런처 업데이트 알림 채널 안내", guild=guild
        )
        async def notices(interaction: discord.Interaction):
            channel = config.notice_channel_id
            await interaction.response.send_message(
                f"업데이트 공지: <#{channel}>\n"
                "해당 채널의 알림 설정에서 ‘모든 메시지’를 선택하세요."
                if channel
                else "업데이트 채널이 아직 설정되지 않았습니다.",
                ephemeral=True,
            )

    async def setup_hook(self):
        if self.application_id != self.config.application_id:
            raise RuntimeError("Discord application mismatch")
        guild = await self.fetch_guild(self.config.guild_id)
        for channel_id in {self.config.notice_channel_id, self.config.internal_channel_id} - {0}:
            channel = await self.fetch_channel(channel_id)
            if not isinstance(channel, discord.TextChannel) or channel.guild.id != guild.id:
                raise RuntimeError("Discord notice channel must belong to configured guild")
            member = await guild.fetch_member(self.user.id)
            permissions = channel.permissions_for(member)
            if not permissions.view_channel or not permissions.send_messages:
                raise RuntimeError("Discord notice channel permissions missing")
        commands = await self.tree.sync(guild=discord.Object(id=self.config.guild_id))
        print(f"Discord commands synced: {len(commands)}", flush=True)

    async def on_ready(self):
        if self.get_guild(self.config.guild_id) is None:
            await self.close()
            return
        print(f"Discord READY: bot={self.user.id}, guild={self.config.guild_id}", flush=True)


async def main():
    try:
        config, settings = Credentials(), Settings()
    except ValueError:
        raise SystemExit("Discord/local API configuration is invalid") from None
    engine, sessions = database(settings)
    client = GeumeumBot(config, settings, sessions)
    channels = {}
    if config.notice_channel_id:
        channels["stable"] = config.notice_channel_id
    if config.internal_channel_id:
        channels["internal"] = config.internal_channel_id

    async def watch_stop():
        while not client.is_closed():
            if Path(".local/discord.stop").exists():
                await client.close()
                return
            await asyncio.sleep(1)

    async def delivery():
        await client.wait_until_ready()
        async with httpx.AsyncClient(
            base_url="https://discord.com/api/v10/",
            timeout=15,
            trust_env=False,
            headers={"Authorization": "Bot " + config.bot_token.get_secret_value()},
        ) as http:
            while not client.is_closed():
                try:
                    if channels:
                        await deliver_one(sessions, http, channels)
                except Exception as exc:
                    print(f"Discord delivery worker error: {type(exc).__name__}", flush=True)
                await asyncio.sleep(5)

    async with client:
        tasks = [asyncio.create_task(watch_stop()), asyncio.create_task(delivery())]
        try:
            await client.start(config.bot_token.get_secret_value())
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            await engine.dispose()
