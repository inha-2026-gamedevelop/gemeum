"""Returning to a live listen host after a client quits or drops out mid-run."""

import struct
import time
import uuid
from datetime import timedelta

import pytest
from fastapi import HTTPException
from test_accounts import auth
from test_launcher_lobby import action, guest, setup_party
from test_relay import packet
from test_relay import relay as relay_fixture

from geumeum_backend.models import RoomMember
from geumeum_backend.rooms import sweep_listen_rooms
from geumeum_backend.security import now

REQUEST = {"challenge": "a" * 64, "build_id": "test-build"}


async def redeem(client, room, key, session):
    ticket = await client.post(f"/v1/rooms/{room}/tickets", headers=auth(session), json=REQUEST)
    assert ticket.status_code == 200, ticket.text
    reply = await client.post(
        f"/internal/rooms/{room}/redeem",
        headers={"X-Server-Key": key},
        json={**REQUEST, "ticket": ticket.json()["ticket"]},
    )
    assert reply.status_code == 200, reply.text
    return reply.json()


async def beat(client, room, key, state, claims):
    return await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": key},
        json={
            "state": state,
            "accounts": [claim["account_id"] for claim in claims],
            "sessions": [
                {"account_id": claim["account_id"], "session_id": claim["session_id"]}
                for claim in claims
            ],
        },
    )


async def seat(app, room, account):
    async with app.state.sessions() as db:
        return await db.get(RoomMember, (uuid.UUID(room), int(account["account"]["account_id"])))


async def running_party(app, client):
    """Host plus one guest, both connected, with the run already under way."""
    app.state.settings.game_hosting = "listen"
    app.state.settings.relay_enabled = True
    owner, room = await setup_party(app, client)
    member = await guest(app, client, "returning")
    await action(client, member, room, "join")
    await action(client, owner, room, "bots", enabled=True)
    for user in (owner, member):
        await action(client, user, room, "preference", preference=0)
        await action(client, user, room, "ready", enabled=True)
    assert (await action(client, owner, room, "start")).json()["state"] == "hosting"

    host_session = (await client.post("/v1/auth/game-sessions", headers=auth(owner))).json()
    host = await client.post(f"/v1/rooms/{room}/host", headers=auth(host_session))
    assert host.status_code == 200, host.text
    key = host.json()["server_key"]
    host_claim = await redeem(client, room, key, host_session)
    assert (await beat(client, room, key, "ready", [host_claim])).status_code == 200

    game = (await client.post("/v1/auth/game-sessions", headers=auth(member))).json()
    relay = await client.post(f"/v1/rooms/{room}/relay", headers=auth(game))
    assert relay.status_code == 200, relay.text
    claim = await redeem(client, room, key, game)
    assert (await beat(client, room, key, "running", [host_claim, claim])).status_code == 200
    return dict(
        owner=owner,
        member=member,
        room=room,
        key=key,
        host=host_claim,
        game=game,
        claim=claim,
        relay=relay.json(),
    )


async def leave_mid_run(app, client, party, absent_seconds=120):
    """The guest's game is gone; the host keeps the run alive without them."""
    room, key, host = party["room"], party["key"], party["host"]
    assert (await beat(client, room, key, "running", [host])).status_code == 200
    async with app.state.sessions() as db:
        row = await db.get(
            RoomMember, (uuid.UUID(room), int(party["member"]["account"]["account_id"]))
        )
        # Emulate the wall clock passing while the host keeps beating without them
        row.expires_at = now() + timedelta(seconds=90 - absent_seconds)
        await db.commit()
    for _ in range(2):
        assert (await beat(client, room, key, "running", [host])).status_code == 200


async def test_host_keeps_the_seat_of_a_launch_member_who_quit(app, client):
    party = await running_party(app, client)
    await leave_mid_run(app, client, party)
    row = await seat(app, party["room"], party["member"])
    assert row is not None, "출발 멤버의 자리가 런 도중 사라지면 돌아올 방법이 없다"
    assert not row.connected
    assert row.expires_at > now() + timedelta(seconds=60)
    listed = (await client.get("/v1/rooms", headers=auth(party["owner"]))).json()["rooms"][0]
    returning = party["member"]["account"]["account_id"]
    assert [m["account_id"] for m in listed["members"] if not m["connected"]] == [returning]


async def test_member_returns_to_the_running_host_with_fresh_relay_credentials(app, client):
    party = await running_party(app, client)
    await leave_mid_run(app, client, party)
    room, key = party["room"], party["key"]

    game = (await client.post("/v1/auth/game-sessions", headers=auth(party["member"]))).json()
    relay = await client.post(f"/v1/rooms/{room}/relay", headers=auth(game))
    assert relay.status_code == 200, relay.text
    assert relay.json()["peer_id"] != party["relay"]["peer_id"]
    assert relay.json()["key"] != party["relay"]["key"]
    assert len(app.state.relay.peers) == 2

    claim = await redeem(client, room, key, game)
    assert claim["account_id"] == party["member"]["account"]["account_id"]
    assert not claim["is_owner"]
    assert (await beat(client, room, key, "running", [party["host"], claim])).status_code == 200
    row = await seat(app, room, party["member"])
    assert row.connected and row.session_id == uuid.UUID(claim["session_id"])


async def test_running_party_still_refuses_accounts_that_did_not_depart(app, client):
    party = await running_party(app, client)
    await leave_mid_run(app, client, party)
    room = party["room"]
    stranger = await guest(app, client, "walkin")
    assert (await action(client, stranger, room, "join")).status_code == 409
    game = (await client.post("/v1/auth/game-sessions", headers=auth(stranger))).json()
    assert (await client.post(f"/v1/rooms/{room}/relay", headers=auth(game))).status_code == 403
    ticket = await client.post(f"/v1/rooms/{room}/tickets", headers=auth(game), json=REQUEST)
    assert ticket.status_code == 403
    assert await seat(app, room, stranger) is None


async def test_returning_relay_slot_is_not_handed_to_another_account(app, client):
    party = await running_party(app, client)
    await leave_mid_run(app, client, party)
    peer = app.state.relay.slots[uuid.UUID(party["room"]), 1]
    assert peer.account == int(party["member"]["account"]["account_id"])
    # The same live session must not be able to rotate its own credentials
    game = (await client.post("/v1/auth/game-sessions", headers=auth(party["member"]))).json()
    assert (
        await client.post(f"/v1/rooms/{party['room']}/relay", headers=auth(game))
    ).status_code == 200
    assert (
        await client.post(f"/v1/rooms/{party['room']}/relay", headers=auth(game))
    ).status_code == 409


def test_a_replaced_relay_slot_survives_a_stale_prune_removal():
    service = relay_fixture()
    stale = service.slots[1, 1]
    service.issue(1, "session-1-1-return", stale.account, 1)
    fresh = service.slots[1, 1]
    assert fresh is not stale and fresh.identity != stale.identity
    # prune() awaits the database between peers, so it can still hold the peer a reconnect
    # just replaced. Releasing it must not strand the peer that took over the slot
    service.remove(stale)
    assert service.slots[1, 1] is fresh
    assert fresh.identity in service.peers and stale.identity not in service.peers
    host = service.slots[1, 0]
    for index, peer in enumerate((host, fresh)):
        service.datagram_received(packet(peer, 1), ("127.0.0.1", 31000 + index))
    service.transport.sent.clear()
    service.datagram_received(packet(host, 2, route=1, payload=b"resume"), host.address)
    assert service.transport.sent[-1][1] == fresh.address


def test_host_relay_slot_is_never_reissued():
    service = relay_fixture()
    host = service.slots[1, 0]
    with pytest.raises(HTTPException):
        service.issue(1, "session-1-0-again", host.account, 0)
    assert service.slots[1, 0] is host


def test_a_returning_guest_only_reclaims_its_own_room_and_slot():
    service = relay_fixture()
    guest_peer = service.slots[1, 1]
    neighbour, other_room = service.slots[1, 2], service.slots[2, 0]
    with pytest.raises(HTTPException):
        service.issue(1, "session-1-1-return", guest_peer.account + 100, 1)
    with pytest.raises(HTTPException):
        service.issue(1, "session-1-1-return", guest_peer.account, 2)
    service.issue(1, "session-1-1-return", guest_peer.account, 1)
    assert service.slots[1, 2] is neighbour and service.slots[2, 0] is other_room
    assert guest_peer.identity not in service.peers


async def test_seat_outlives_the_game_session_so_the_room_is_still_findable(app, client):
    party = await running_party(app, client)
    await leave_mid_run(app, client, party)
    room, returning = party["room"], party["member"]["account"]["account_id"]
    # Quitting the game may discard its session; the launcher session that joined remains
    await client.post("/v1/auth/logout", headers=auth(party["game"]))
    listed = (await client.get("/v1/rooms", headers=auth(party["owner"]))).json()["rooms"][0]
    assert returning in {m["account_id"] for m in listed["members"]}
    game = (await client.post("/v1/auth/game-sessions", headers=auth(party["member"]))).json()
    assert (await client.post(f"/v1/rooms/{room}/relay", headers=auth(game))).status_code == 200
    claim = await redeem(client, room, party["key"], game)
    assert claim["account_id"] == returning and not claim["is_owner"]


async def test_revoked_session_loses_the_connection_but_keeps_the_seat_mid_run(app, client):
    party = await running_party(app, client)
    room, key, returning = party["room"], party["key"], party["member"]["account"]["account_id"]
    # The session dies between redeem and the next heartbeat: revoke the peer, hold the seat
    await client.post("/v1/auth/logout", headers=auth(party["game"]))
    reply = await beat(client, room, key, "running", [party["host"], party["claim"]])
    assert reply.json()["revoked"] == [returning]
    row = await seat(app, room, party["member"])
    assert row is not None and not row.connected
    assert (await beat(client, room, key, "running", [party["host"]])).status_code == 200
    row = await seat(app, room, party["member"])
    assert row.expires_at > now() + timedelta(seconds=60)
    game = (await client.post("/v1/auth/game-sessions", headers=auth(party["member"]))).json()
    assert (await client.post(f"/v1/rooms/{room}/relay", headers=auth(game))).status_code == 200


def notices(service, host):
    """Decode every seat notice the relay sent the host, newest last."""
    found = []
    for sent, _ in service.transport.sent:
        plain = host.cipher.decrypt(b"DN01" + sent[20:28], sent[28:], sent[:28])
        if struct.unpack_from("<I", plain)[0] == 0 and len(plain) == 8:
            found.append(
                (struct.unpack_from("<Q", sent, 20)[0], struct.unpack_from("<I", plain, 4)[0])
            )
    return found


def woken(service, room=1):
    """A relay whose host peer has an address, so notices can actually be sent."""
    host = service.slots[room, 0]
    service.datagram_received(packet(host, 1), ("127.0.0.1", 32000))
    service.transport.sent.clear()
    return host


def test_reissuing_a_seat_announces_it_to_the_host_with_an_ordered_sequence():
    service = relay_fixture()
    host = woken(service)
    service.issue(1, "session-1-1-return", service.slots[1, 1].account, 1)
    assert [slot for _, slot in notices(service, host)] == [1]
    # 돌아온 손님의 첫 패킷은 통지보다 반드시 뒤 번호로 호스트에 전달된다.
    # 호스트는 그 순서만 보면 늦게 도착한 통지를 새 연결에 적용하지 않을 수 있다
    notice_sequence = notices(service, host)[0][0]
    fresh = service.slots[1, 1]
    service.datagram_received(packet(fresh, 1, payload=b"hello"), ("127.0.0.1", 33000))
    forwarded = struct.unpack_from("<Q", service.transport.sent[-1][0], 20)[0]
    assert forwarded > notice_sequence


def test_seat_notice_repeats_on_host_keepalive_until_the_returning_peer_answers():
    service = relay_fixture()
    host = woken(service)
    service.issue(1, "session-1-1-return", service.slots[1, 1].account, 1)
    for _ in range(3):
        service.datagram_received(packet(host, service.slots[1, 0].maximum + 1), host.address)
    assert len(notices(service, host)) == 4, "한 번 유실돼도 다음 keepalive가 다시 알린다"
    # 새 자격의 peer가 응답하는 순간 통지를 멈춘다. 이후 keepalive는 아무것도 싣지 않는다
    service.datagram_received(packet(service.slots[1, 1], 1, payload=b"hi"), ("127.0.0.1", 33001))
    service.transport.sent.clear()
    service.datagram_received(packet(host, host.maximum + 1), host.address)
    assert not notices(service, host)
    assert (1, 1) not in service.pending


def test_seat_notice_stops_after_its_window_even_if_nobody_returns():
    service = relay_fixture()
    host = woken(service)
    service.issue(1, "session-1-1-return", service.slots[1, 1].account, 1)
    service.pending[1, 1] = time.monotonic() - 1
    service.transport.sent.clear()
    service.datagram_received(packet(host, host.maximum + 1), host.address)
    assert not notices(service, host)
    assert (1, 1) not in service.pending


def test_no_seat_notice_without_a_replaced_seat_or_a_reachable_host():
    service = relay_fixture()
    host = woken(service)
    service.issue(3, "session-3-1", 31, 1)  # 새 방의 첫 발급이라 교체할 자리가 없다
    assert not notices(service, host)
    quiet = relay_fixture()  # 호스트가 아직 한 번도 보내지 않아 주소를 모른다
    quiet.issue(1, "session-1-2-return", quiet.slots[1, 2].account, 2)
    assert not quiet.transport.sent
    assert quiet.pending[1, 2] > time.monotonic()


def test_seat_notices_do_not_outlive_the_host_they_were_meant_for():
    service = relay_fixture()
    woken(service)
    service.issue(1, "session-1-1-return", service.slots[1, 1].account, 1)
    assert (1, 1) in service.pending
    # 호스트가 사라지면 그 통지를 가져갈 사람이 없다. announce는 그 방을 다시 보지 않는다
    service.remove(service.slots[1, 0])
    service.expire_notices()
    assert (1, 1) not in service.pending


def test_seat_notices_expire_on_the_sweep_even_if_the_host_stops_pinging():
    service = relay_fixture()
    woken(service)
    service.issue(1, "session-1-1-return", service.slots[1, 1].account, 1)
    service.issue(1, "session-1-2-return", service.slots[1, 2].account, 2)
    service.pending[1, 1] = time.monotonic() - 1
    service.expire_notices()
    assert (1, 1) not in service.pending
    assert (1, 2) in service.pending, "아직 창이 남은 통지는 유지한다"


async def test_room_sweep_is_the_path_that_clears_abandoned_seat_notices(app, client):
    app.state.settings.relay_enabled = True
    stranded = uuid.uuid4()
    app.state.relay.pending[stranded, 1] = time.monotonic() + 999
    await sweep_listen_rooms(app)
    assert (stranded, 1) not in app.state.relay.pending
