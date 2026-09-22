import struct
import time
from types import SimpleNamespace

import pytest
from fastapi import HTTPException
from test_accounts import auth
from test_launcher_lobby import action, guest, setup_party

from geumeum_backend.relay import MAGIC, Relay
from geumeum_backend.rooms import sweep_listen_rooms


class Wire:
    def __init__(self):
        self.sent = []

    def sendto(self, packet, address):
        self.sent.append((packet, address))


def packet(peer, sequence, route=0, payload=b"", direction=b"UP01"):
    seq = struct.pack("<Q", sequence)
    header = MAGIC + peer.identity + seq
    return header + peer.cipher.encrypt(direction + seq, struct.pack("<I", route) + payload, header)


def relay():
    service = Relay(
        SimpleNamespace(
            relay_bytes_per_second=8_000_000,
            game_max_rooms=3,
            relay_public_address="127.0.0.1",
            relay_port=18001,
        )
    )
    service.transport = Wire()
    for room, slot in [(1, 0), (1, 1), (1, 2), (2, 0)]:
        service.issue(room, f"session-{room}-{slot}", slot + 1, slot)
    return service


def test_authenticated_encrypted_routing_replay_and_room_isolation():
    service = relay()
    host, first, second = [service.slots[1, i] for i in range(3)]
    for index, peer in enumerate((host, first, second)):
        service.datagram_received(packet(peer, 1), ("127.0.0.1", 30000 + index))
    service.transport.sent.clear()
    original = packet(first, 3, payload=b"client kcp")
    service.datagram_received(original, first.address)
    response, address = service.transport.sent[-1]
    assert address == host.address
    assert host.cipher.decrypt(b"DN01" + response[20:28], response[28:], response[:28]) == (
        struct.pack("<I", 1) + b"client kcp"
    )
    count = len(service.transport.sent)
    service.datagram_received(original, first.address)  # replay
    service.datagram_received(original[:-1] + bytes([original[-1] ^ 1]), first.address)
    service.datagram_received(packet(first, 4, payload=b"spoof"), ("127.0.0.2", 40000))
    service.datagram_received(packet(first, 5, route=2, payload=b"guest to guest"), first.address)
    service.datagram_received(
        packet(first, 6, payload=b"reflection", direction=b"DN01"), first.address
    )
    service.datagram_received(b"GR01" + b"x" * 1245, first.address)
    assert len(service.transport.sent) == count
    service.datagram_received(packet(first, 2, payload=b"reordered"), first.address)
    assert len(service.transport.sent) == count + 1
    service.datagram_received(packet(host, 2, route=2, payload=b"reply"), host.address)
    assert service.transport.sent[-1][1] == second.address
    with pytest.raises(HTTPException):
        service.issue(1, "other session", 9, 1)
    first.valid_until = time.monotonic() - 1
    count = len(service.transport.sent)
    service.datagram_received(packet(first, 10, payload=b"expired"), first.address)
    assert len(service.transport.sent) == count


def test_rate_limit_and_replay_window_are_bounded():
    service = relay()
    peer = service.slots[1, 1]
    assert peer.accept(2000)
    assert not peer.accept(1)
    assert peer.accept(1999)
    assert not peer.accept(1999)
    assert peer.seen.bit_length() <= 1024
    peer.traffic.balance = 0
    peer.traffic.updated = time.monotonic() + 10
    service.datagram_received(packet(peer, 2001), ("127.0.0.1", 1))
    assert not service.transport.sent


async def test_relay_credentials_require_departed_game_session_and_revoke(app, client):
    app.state.settings.game_hosting = "listen"
    app.state.settings.relay_enabled = True
    owner, room = await setup_party(app, client)
    member = await guest(app, client, "relaymember")
    stranger = await guest(app, client, "relaystranger")
    await action(client, member, room, "join")
    await action(client, owner, room, "bots", enabled=True)
    for user in [owner, member]:
        await action(client, user, room, "preference", preference=0)
        await action(client, user, room, "ready", enabled=True)
    await action(client, owner, room, "start")
    games = [
        (await client.post("/v1/auth/game-sessions", headers=auth(user))).json()
        for user in [owner, member, stranger]
    ]
    host = await client.post(f"/v1/rooms/{room}/host", headers=auth(games[0]))
    assert host.status_code == 200
    assert len(host.json()["relay"]["key"]) == 64
    await client.post(
        f"/internal/rooms/{room}/heartbeat",
        headers={"X-Server-Key": host.json()["server_key"]},
        json={"state": "ready", "accounts": []},
    )
    endpoint = f"/v1/rooms/{room}/relay"
    assert (await client.post(endpoint, headers=auth(member))).status_code == 403
    assert (await client.post(endpoint, headers=auth(games[2]))).status_code == 403
    assert (await client.post(endpoint, headers=auth(games[1]))).status_code == 200
    assert (await client.post(endpoint, headers=auth(games[1]))).status_code == 409
    listing = (await client.get("/v1/rooms", headers=auth(owner))).text
    assert host.json()["relay"]["key"] not in listing
    await client.post("/v1/auth/logout", headers=auth(member))
    await sweep_listen_rooms(app)
    assert len(app.state.relay.peers) == 1
    await client.delete(f"/v1/rooms/{room}", headers=auth(owner))
    await sweep_listen_rooms(app)
    assert not app.state.relay.peers
