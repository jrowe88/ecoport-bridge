"""Checksum calculation for CTA-2045 link-layer frames.

CTA-2045 frames end with a two-byte Fletcher-style checksum seeded with
``0xAA``. The algorithm below reproduces the trailing two bytes of every
frame captured from the Rinnai REHP65 (see docs/protocol.md, Confirmed).
"""


def compute_checksum(data: bytes) -> bytes:
    """Return the two checksum bytes (MSB, LSB) for ``data``.

    ``data`` is the frame header and payload, excluding the checksum itself.
    """
    c1 = 0xAA
    c2 = 0
    for byte in data:
        c1 = (c1 + byte) % 255
        c2 = (c2 + c1) % 255
    msb = 255 - ((c1 + c2) % 255)
    lsb = 255 - ((c1 + msb) % 255)
    return bytes((msb, lsb))


def verify_checksum(frame: bytes) -> bool:
    """Return True if the last two bytes of ``frame`` are a valid checksum."""
    return len(frame) >= 2 and compute_checksum(frame[:-2]) == frame[-2:]
