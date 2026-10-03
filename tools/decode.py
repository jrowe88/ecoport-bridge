#!/usr/bin/env python3
"""Decode a saved passive capture into CTA-2045 frames (offline, never transmits).

Usage:
    python tools/decode.py captures/rinnai/<capture-dir>
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python" / "src"))

from ecoport.cta2045.framing import split_frames

MESSAGE_TYPES = {
    b"\x08\x01": "Basic DR",
    b"\x08\x02": "Intermediate DR",
    b"\x08\x03": "Data-Link",
    b"\x08\x04": "Commissioning/Network",
}

DATA_LINK_OPCODES = {
    0x16: "Request Different Power Mode",
    0x17: "Request Different Bit Rate",
    0x18: "Query: Maximum Payload Length",
    0x19: "Response: Maximum Payload Length",
    0x1A: "Query: Get SGD Slot Number",
    0x1B: "Response: Slot Number",
}


BASIC_OPCODES = {
    0x01: "Shed",
    0x02: "End Shed",
    0x03: "App ACK",
    0x04: "App NAK",
    0x0E: "Outside Comm Status",
    0x11: "Customer Override",
    0x12: "Query Operational State",
    0x13: "Operational State",
    0x14: "Sleep",
    0x15: "Wake/Refresh",
}


def describe_raw(raw: bytes) -> str:
    if raw == b"\x06\x00":
        return "Link ACK"
    if len(raw) == 2 and raw[0] == 0x15:
        return f"Link NAK code 0x{raw[1]:02X}"
    return describe(raw[:2], raw[4:-2])


def describe(msg_type: bytes, payload: bytes) -> str:
    name = MESSAGE_TYPES.get(msg_type, f"type {msg_type.hex(' ')}")
    if not payload:
        return f"Message Type Supported Query ({name})"
    if msg_type == b"\x08\x03" and len(payload) == 2:
        op = DATA_LINK_OPCODES.get(payload[0], f"opcode 0x{payload[0]:02X}")
        return f"{name}: {op} (opcode2 0x{payload[1]:02X})"
    if msg_type == b"\x08\x01" and len(payload) == 2:
        op = BASIC_OPCODES.get(payload[0], f"opcode 0x{payload[0]:02X}")
        return f"{name}: {op} (opcode2 0x{payload[1]:02X})"
    return f"{name}: payload {payload.hex(' ')}"


def tx_events(capture_dir: Path) -> list[tuple[datetime, str, bytes]]:
    path = capture_dir / "transmit.jsonl"
    if not path.exists():
        return []
    events = []
    for line in path.read_text(encoding="utf-8").splitlines():
        event = json.loads(line)
        if event.get("direction", "tx") == "tx":
            events.append((datetime.fromisoformat(event["timestamp"]), "TX", bytes.fromhex(event["hex"])))
    return events


def byte_times(capture_dir: Path, size: int) -> list[datetime | None]:
    times: list[datetime | None] = [None] * size
    jsonl = capture_dir / "capture.jsonl"
    if not jsonl.exists():
        return times
    for line in jsonl.read_text(encoding="utf-8").splitlines():
        event = json.loads(line)
        ts = datetime.fromisoformat(event["timestamp"])
        for i in range(event["offset"], min(event["offset"] + event["length"], size)):
            times[i] = ts
    return times


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("capture_dir", type=Path)
    parser.add_argument("--burst-gap", type=float, default=5.0,
                        help="seconds of silence that start a new burst (default 5)")
    args = parser.parse_args(argv)

    data = (args.capture_dir / "capture.bin").read_bytes()
    frames, junk = split_frames(data)
    times = byte_times(args.capture_dir, len(data))
    print(f"{len(data)} bytes, {len(frames)} valid frames, {len(junk)} junk runs")
    if not frames:
        return 0

    events = [(times[f.offset], "RX", f.raw) for f in frames if times[f.offset]]
    events += tx_events(args.capture_dir)
    events.sort(key=lambda e: e[0])
    t0 = events[0][0]
    prev = None
    for t, direction, raw in events:
        rel = (t - t0).total_seconds()
        if prev is None or rel - prev > args.burst_gap:
            print(f"--- burst at +{rel:.1f}s")
        prev = rel
        print(f"  +{rel:7.1f}s {direction} {raw.hex(' '):<28} {describe_raw(raw)}")

    print("\nFrame counts:")
    for raw, n in Counter(f.raw for f in frames).most_common():
        print(f"  {n:4d} x {raw.hex(' ')}")
    for offset, run in junk:
        print(f"junk at offset {offset}: {run.hex(' ')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
