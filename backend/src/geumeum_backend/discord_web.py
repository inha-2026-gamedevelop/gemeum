from pathlib import Path

from fastapi import HTTPException, Request
from fastapi.responses import HTMLResponse, Response
from pydantic import ValidationError

from .discord_accounts import SetupAction, apply_action, linked_profile, ticket


def install(app):
    from .api import throttle

    @app.get("/discord/account", response_class=HTMLResponse)
    async def page():
        return HTMLResponse(
            Path(__file__).with_name("discord_account.html").read_text("utf-8"),
            headers={
                "Content-Security-Policy": (
                    "default-src 'self'; style-src 'unsafe-inline'; "
                    "frame-ancestors 'none'; form-action 'none'"
                ),
                "Referrer-Policy": "no-referrer",
            },
        )

    @app.get("/discord/account.js")
    async def script():
        return Response(
            Path(__file__).with_name("discord_account.js").read_text("utf-8"),
            media_type="application/javascript",
        )

    async def proof(request):
        if request.headers.get("origin") != app.state.settings.account_origin:
            raise HTTPException(403, "설정 페이지에서 요청해 주세요.")
        raw = request.headers.get("authorization", "").removeprefix("Bearer ")
        if not 40 <= len(raw) <= 128:
            raise HTTPException(401, "Discord에서 /계정 링크를 발급하세요.")
        await throttle(request, [("discord-setup-peer", 60)])
        return raw

    @app.post("/discord/context")
    async def context(request: Request):
        raw = await proof(request)
        async with app.state.sessions() as db:
            item = await ticket(db, raw)
            account = await linked_profile(db, item.discord_id)
            return {"account": account}

    @app.post("/discord/apply")
    async def apply(request: Request, data: SetupAction):
        raw = await proof(request)
        if data.action == "link":
            await throttle(request, [("discord-link:" + data.login_id.strip().lower(), 10)])
        async with app.state.sessions() as db:
            try:
                account = await apply_action(db, app.state.passwords, raw, data)
            except ValidationError:
                raise HTTPException(
                    422,
                    "아이디 4~24자(영문/숫자/_), 닉네임 2~16자, 비밀번호 8~15자를 확인하세요.",
                ) from None
            return {
                "account": account,
                "message": "저장했습니다. 추가 변경은 Discord에서 /계정을 다시 실행하세요.",
            }
