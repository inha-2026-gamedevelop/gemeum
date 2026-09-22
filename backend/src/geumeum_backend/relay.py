"""Bounded, authenticated UDP relay. Gameplay authority stays on the listen host.

GR01 | peer ID (16 bytes) | sequence (uint64 LE) | AES-256-GCM ciphertext/tag.
AAD is the 28-byte header; nonce is UP01/DN01 + sequence. Plaintext is
destination/source slot (uint32 LE) + KCP datagram (empty means keepalive).
Fresh, one-use HTTPS credentials prevent nonce reuse across process restarts.

Downstream source 0 is the relay itself, never a guest, so it carries one control
frame: a 4-byte body naming a slot whose credentials were just reissued. The host
uses it to release the connection it still holds for that slot. Sequence numbers
totally order everything the relay sends a peer, so the host can ignore a notice
that is older than data it already accepted for that slot; a lost notice only costs
the transport timeout it used to wait anyway. Gameplay authority is untouched - the
frame can only make the host drop a connection, never admit one.
"""

import asyncio
import secrets
import struct
import time
from dataclasses import dataclass, field

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from fastapi import HTTPException

MAGIC = b"GR01"
MAX_PACKET = 1248
WINDOW = 1024
# Repeat a seat notice for this long so a single lost datagram does not cost the timeout
NOTICE_SECONDS = 5


@dataclass
class Bucket:
    rate: int
    balance: float = 0
    updated: float = field(default_factory=time.monotonic)

    def take(self, amount):
        stamp = time.monotonic()
        self.balance = min(self.rate * 2, self.balance + (stamp - self.updated) * self.rate)
        self.updated = stamp
        if amount > self.balance:
            return False
        self.balance -= amount
        return True


@dataclass
class Peer:
    room: object
    session: object
    account: int
    slot: int
    key: bytes = field(default_factory=lambda: secrets.token_bytes(32))
    identity: bytes = field(default_factory=lambda: secrets.token_bytes(16))
    address: tuple | None = None
    maximum: int = 0
    seen: int = 0
    outgoing: int = 0
    valid_until: float = field(default_factory=lambda: time.monotonic() + 10)
    traffic: Bucket = field(default_factory=lambda: Bucket(512_000, 1_024_000))
    packets: Bucket = field(default_factory=lambda: Bucket(1000, 2000))

    def __post_init__(self):
        self.cipher = AESGCM(self.key)

    def accept(self, sequence):
        if sequence == 0 or sequence + WINDOW <= self.maximum:
            return False
        if sequence > self.maximum:
            self.seen = (self.seen << min(WINDOW, sequence - self.maximum)) & ((1 << WINDOW) - 1)
            self.maximum = sequence
        bit = 1 << (self.maximum - sequence)
        if self.seen & bit:
            return False
        self.seen |= bit
        return True


class Relay(asyncio.DatagramProtocol):
    def __init__(self, settings):
        self.settings = settings
        self.peers = {}
        self.slots = {}
        self.pending = {}
        self.transport = None
        self.traffic = Bucket(settings.relay_bytes_per_second, settings.relay_bytes_per_second * 2)
        self.packets = Bucket(20_000, 40_000)
        self.forwarded_bytes = 0

    def connection_made(self, transport):
        self.transport = transport

    def issue(self, room, session, account, slot):
        held = self.slots.get((room, slot))
        if held is not None:
            # A new game session of the same account is that person coming back to a live
            # host: drop the abandoned peer so the seat gets fresh keys, never reused ones.
            # Slot 0 is the listen host itself and stays single-issue: a second host would
            # split gameplay authority, and /host already refuses a second claim
            if slot == 0 or held.account != account or held.session == session:
                raise HTTPException(409, "Relay connection already issued; create a new party")
            self.remove(held)
            self.pending[room, slot] = time.monotonic() + NOTICE_SECONDS
            self.announce(room)
        if len(self.peers) >= self.settings.game_max_rooms * 3:
            raise HTTPException(503, "Relay capacity reached")
        peer = Peer(room, session, account, slot)
        self.peers[peer.identity] = peer
        self.slots[room, slot] = peer
        return dict(
            enabled=True,
            address=self.settings.relay_public_address,
            port=self.settings.relay_port,
            peer_id=peer.identity.hex(),
            key=peer.key.hex(),
        )

    def announce(self, room):
        """Tell the listen host which seats were reissued, until the new peer shows up.

        The host still holds a KCP connection numbered with the old slot and will not
        answer the returning client until that connection times out. Notices repeat on
        the host's keepalive so one lost datagram is not fatal, and they stop the moment
        the new peer sends anything, which is strictly before the host can have finished
        a handshake with it. Every copy therefore carries a sequence lower than the
        returning client's first packet, and the host drops only the connection it
        already had.
        """
        host = self.slots.get((room, 0))
        if self.transport is None or host is None or host.address is None:
            return
        self.expire_notices()
        for key in list(self.pending):
            if key[0] == room:
                self.send(host, 0, struct.pack("<I", key[1]))

    def expire_notices(self):
        """Drop seat notices nobody will ever collect.

        announce() only ever visits the room it was called for, so a party whose host
        stopped pinging - or closed - would keep its entries forever. The room sweep
        calls this on every pass, which is the only path that sees those rooms again.
        """
        stamp = time.monotonic()
        for key in list(self.pending):
            if self.pending[key] <= stamp or (key[0], 0) not in self.slots:
                del self.pending[key]

    def remove(self, peer):
        self.peers.pop(peer.identity, None)
        # Only release the slot if it is still this peer. prune() awaits the database
        # between checks, so a reconnect can replace the slot underneath it; dropping the
        # entry blindly would strand the peer that just took over
        if self.slots.get((peer.room, peer.slot)) is peer:
            del self.slots[peer.room, peer.slot]

    def datagram_received(self, data, address):
        if not 48 <= len(data) <= MAX_PACKET or data[:4] != MAGIC:
            return
        if not self.packets.take(1) or not self.traffic.take(len(data)):
            return
        peer = self.peers.get(data[4:20])
        if (
            peer is None
            or peer.valid_until <= time.monotonic()
            or (peer.address is not None and peer.address != address)
            or not peer.packets.take(1)
            or not peer.traffic.take(len(data))
        ):
            return
        try:
            plain = peer.cipher.decrypt(b"UP01" + data[20:28], data[28:], data[:28])
        except InvalidTag:
            return
        if len(plain) < 4 or not peer.accept(struct.unpack_from("<Q", data, 20)[0]):
            return
        peer.address = address
        # The seat is answering again, so nothing about it is stale any more
        self.pending.pop((peer.room, peer.slot), None)
        route = struct.unpack_from("<I", plain)[0]
        if len(plain) == 4:
            self.send(peer, 0, b"")
            if peer.slot == 0:
                self.announce(peer.room)
            return
        if (peer.slot == 0 and route not in {1, 2}) or (peer.slot != 0 and route != 0):
            return
        target = self.slots.get((peer.room, route))
        if target and target.address and target.valid_until > time.monotonic():
            self.send(target, peer.slot, plain[4:])

    def send(self, peer, source, payload):
        peer.outgoing += 1
        sequence = struct.pack("<Q", peer.outgoing)
        header = MAGIC + peer.identity + sequence
        packet = header + peer.cipher.encrypt(
            b"DN01" + sequence, struct.pack("<I", source) + payload, header
        )
        self.transport.sendto(packet, peer.address)
        self.forwarded_bytes += len(packet)

    async def prune(self, db):
        from .accounts import session_valid
        from .models import Account, AuthSession, Room

        self.expire_notices()
        for peer in list(self.peers.values()):
            room = await db.get(Room, peer.room)
            session = await db.get(AuthSession, peer.session)
            account = await db.get(Account, peer.account)
            if (
                room is None
                or room.state == "closed"
                or not session
                or not account
                or not await session_valid(db, account, session)
            ):
                self.remove(peer)
            else:
                peer.valid_until = time.monotonic() + 10

    async def start(self):
        await asyncio.get_running_loop().create_datagram_endpoint(
            lambda: self, local_addr=(self.settings.relay_bind_address, self.settings.relay_port)
        )

    def close(self):
        if self.transport:
            self.transport.close()
        self.peers.clear()
        self.slots.clear()
        self.pending.clear()
