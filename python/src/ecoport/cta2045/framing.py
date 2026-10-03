"""CTA-2045 link-layer framing.

Two packet shapes exist on the wire (CTA-2045-B §6.1, §8.1):

* Link-layer ACK/NAK: exactly 2 bytes, ``06 00`` or ``15 <code>``.
* Message frame: ``msg_type (2) + payload_length (2, big-endian) + payload +
  Fletcher checksum (2)``.

Both are confirmed against Rinnai REHP65 captures and the spec examples.
"""

from __future__ import annotations

from dataclasses import dataclass

from .crc import compute_checksum, verify_checksum

HEADER_LEN = 4
CHECKSUM_LEN = 2
LINK_ACK = b"\x06\x00"
LINK_NAK_PREFIX = 0x15
MAX_NAK_CODE = 0x07
LENGTH_MASK = 0x1FFF


@dataclass(frozen=True)
class Frame:
    offset: int
    raw: bytes

    @property
    def is_link_ack(self) -> bool:
        return self.raw == LINK_ACK

    @property
    def is_link_nak(self) -> bool:
        return len(self.raw) == 2 and self.raw[0] == LINK_NAK_PREFIX

    @property
    def is_link_control(self) -> bool:
        return self.is_link_ack or self.is_link_nak

    @property
    def msg_type(self) -> bytes:
        return b"" if self.is_link_control else self.raw[:2]

    @property
    def payload(self) -> bytes:
        return b"" if self.is_link_control else self.raw[HEADER_LEN:-CHECKSUM_LEN]

    @property
    def checksum_ok(self) -> bool:
        return self.is_link_control or verify_checksum(self.raw)


def frame(msg_type: bytes, payload: bytes = b"") -> bytes:
    """Build a complete frame (header, payload, checksum) as bytes."""
    if len(msg_type) != 2:
        raise ValueError("msg_type must be exactly 2 bytes")
    body = msg_type + len(payload).to_bytes(2, "big") + payload
    return body + compute_checksum(body)


def unframe(data: bytes) -> bytes:
    """Validate a single complete frame and return its payload."""
    if len(data) < HEADER_LEN + CHECKSUM_LEN:
        raise ValueError("frame too short")
    length = int.from_bytes(data[2:4], "big") & LENGTH_MASK
    if len(data) != HEADER_LEN + length + CHECKSUM_LEN:
        raise ValueError("frame length does not match header")
    if not verify_checksum(data):
        raise ValueError("bad checksum")
    return data[HEADER_LEN:-CHECKSUM_LEN]


def _match(data: bytes, i: int) -> int | None:
    """Return the end index of a valid packet at ``i``.

    Returns ``-1`` if more bytes are needed and ``None`` if no valid packet
    can start at ``i``.
    """
    remaining = len(data) - i
    first = data[i]
    if first in (0x06, LINK_NAK_PREFIX):
        if remaining < 2:
            return -1
        second = data[i + 1]
        if (first == 0x06 and second == 0x00) or (
            first == LINK_NAK_PREFIX and 0x01 <= second <= MAX_NAK_CODE
        ):
            return i + 2
        return None
    if remaining < HEADER_LEN:
        return -1
    length = int.from_bytes(data[i + 2 : i + 4], "big") & LENGTH_MASK
    end = i + HEADER_LEN + length + CHECKSUM_LEN
    if end > len(data):
        return -1
    return end if verify_checksum(data[i:end]) else None


MAX_PLAUSIBLE_PAYLOAD = 4096


def _plausible_start(buffer: bytes) -> bool:
    """True if an incomplete packet at the buffer start could be real.

    Standard message types begin with 0x08 or 0x09 (§6.1.1); ACK/NAK begin
    with 0x06/0x15.
    """
    if buffer[0] in (0x06, LINK_NAK_PREFIX):
        return True
    if buffer[0] not in (0x08, 0x09):
        return False
    if len(buffer) < HEADER_LEN:
        return True
    return int.from_bytes(buffer[2:4], "big") & LENGTH_MASK <= MAX_PLAUSIBLE_PAYLOAD


def split_frames(data: bytes) -> tuple[list[Frame], list[tuple[int, bytes]]]:
    """Split a complete raw byte stream into valid packets.

    Returns ``(frames, junk)`` where ``junk`` lists ``(offset, bytes)`` runs
    that could not be resynchronised into a valid packet.
    """
    frames: list[Frame] = []
    junk: list[tuple[int, bytes]] = []
    i = 0
    junk_start: int | None = None
    while i < len(data):
        end = _match(data, i)
        if end is not None and end > 0:
            if junk_start is not None:
                junk.append((junk_start, data[junk_start:i]))
                junk_start = None
            frames.append(Frame(i, data[i:end]))
            i = end
            continue
        if junk_start is None:
            junk_start = i
        i += 1
    if junk_start is not None:
        junk.append((junk_start, data[junk_start:]))
    return frames, junk


class StreamParser:
    """Incrementally parse packets from a live byte stream."""

    def __init__(self) -> None:
        self._buffer = bytearray()
        self._offset = 0
        self.dropped = bytearray()

    @property
    def pending(self) -> bytes:
        return bytes(self._buffer)

    def feed(self, data: bytes) -> list[Frame]:
        self._buffer.extend(data)
        frames: list[Frame] = []
        while self._buffer:
            buffer = bytes(self._buffer)
            end = _match(buffer, 0)
            if end == -1:
                if _plausible_start(buffer):
                    break
                skip = next(
                    (j for j in range(1, len(buffer)) if (_match(buffer, j) or 0) > 0), None
                )
                if skip is None:
                    break
                self._drop(skip)
                continue
            if end is None:
                self._drop(1)
                continue
            frames.append(Frame(self._offset, bytes(self._buffer[:end])))
            del self._buffer[:end]
            self._offset += end
        return frames

    def flush_stale(self) -> bytes:
        """Discard a partial packet (call after an inter-byte timeout)."""
        stale = bytes(self._buffer)
        self._drop(len(stale))
        return stale

    def _drop(self, count: int) -> None:
        self.dropped.extend(self._buffer[:count])
        del self._buffer[:count]
        self._offset += count
