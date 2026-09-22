"""Local Discord bot; never log interaction payloads or credentials."""

import asyncio
import sys

from geumeum_backend.discord_bot import main

if __name__ == "__main__":
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    try:
        asyncio.run(main())
    except Exception as exc:
        print(f"Discord bot stopped: {type(exc).__name__}", flush=True)
        raise SystemExit(1) from None
