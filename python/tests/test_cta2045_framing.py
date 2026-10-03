"""Tests for CTA-2045 framing and checksum, using frames seen on the Rinnai."""

from ecoport.cta2045.crc import compute_checksum, verify_checksum
from ecoport.cta2045.framing import frame, split_frames, unframe

OBSERVED = [
    "08 01 00 00 7e cd",
    "08 02 00 00 7a d0",
    "08 03 00 00 76 d3",
    "08 03 00 02 18 00 ba 75",
]


def test_checksum_matches_observed_and_spec_example_frames():
    for text in OBSERVED + ["08 04 00 00 72 d6", "08 03 00 02 16 00 c0 71"]:
        raw = bytes.fromhex(text)
        assert compute_checksum(raw[:-2]) == raw[-2:]
        assert verify_checksum(raw)


def test_frame_round_trip():
    raw = frame(b"\x08\x03", b"\x18\x00")
    assert raw == bytes.fromhex(OBSERVED[3])
    assert unframe(raw) == b"\x18\x00"


def test_split_frames_resyncs_past_junk():
    stream = b"\xff" + b"".join(bytes.fromhex(t) for t in OBSERVED) + b"\x08"
    frames, junk = split_frames(stream)
    assert [f.raw.hex(" ") for f in frames] == OBSERVED
    assert junk == [(0, b"\xff"), (len(stream) - 1, b"\x08")]
