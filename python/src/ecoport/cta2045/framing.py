"""CTA-2045 link-layer framing.

A frame is ``msg_type (2 bytes) + payload_length (2 bytes, big-endian) +
payload + checksum (2 bytes)``. Confirmed against Rinnai REHP65 captures.
"""

from __future__ import annotations

from dataclasses import dataclass

from .crc import compute_checksum, verify_checksum

HEADER_LEN = 4
CHECKSUM_LEN = 2


@dataclass(frozen=True)
class Frame:
    offset: int
    raw: bytes

    @property
    def msg_type(self) -> bytes:
        return self.raw[:2]

    @property
    def payload(self) -> bytes:
        return self.raw[HEADER_LEN:-CHECKSUM_LEN]

    @property
    def checksum_ok(self) -> bool:
        return verify_checksum(self.raw)


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
    length = int.from_bytes(data[2:4], "big")
    if len(data) != HEADER_LEN + length + CHECKSUM_LEN:
        raise ValueError("frame length does not match header")
    if not verify_checksum(data):
        raise ValueError("bad checksum")
    return data[HEADER_LEN:-CHECKSUM_LEN]


def split_frames(data: bytes) -> tuple[list[Frame], list[tuple[int, bytes]]]:
    """Split a raw byte stream into checksum-valid frames.

    Returns ``(frames, junk)`` where ``junk`` lists ``(offset, bytes)`` runs
    that could not be resynchronised into a valid frame.
    """
    frames: list[Frame] = []
    junk: list[tuple[int, bytes]] = []
    i = 0
    junk_start: int | None = None
    while i < len(data):
        if i + HEADER_LEN + CHECKSUM_LEN <= len(data):
            length = int.from_bytes(data[i + 2 : i + 4], "big")
            end = i + HEADER_LEN + length + CHECKSUM_LEN
            if end <= len(data) and verify_checksum(data[i:end]):
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
