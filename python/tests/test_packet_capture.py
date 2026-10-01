"""Tests for the raw capture artifact format."""

import base64
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))

from packet_capture import CaptureWriter


def test_capture_writer_preserves_raw_bytes_and_event_metadata(tmp_path):
    started_at = datetime(2026, 10, 1, 16, 0, 0, tzinfo=timezone.utc)

    with CaptureWriter(
        tmp_path,
        "Heat Pump Only",
        {"serial_port": "COM3"},
        started_at=started_at,
    ) as capture:
        capture.record(b"\x08\x01", received_at=started_at)
        capture.record(b"\xff")

    assert capture.directory.name == "2026-10-01T160000-heat-pump-only"
    assert capture.binary_path.read_bytes() == b"\x08\x01\xff"

    events = [
        json.loads(line)
        for line in capture.events_path.read_text(encoding="utf-8").splitlines()
    ]
    assert events[0]["offset"] == 0
    assert events[0]["hex"] == "08 01"
    assert events[1]["offset"] == 2
    assert base64.b64decode(events[1]["base64"]) == b"\xff"
    assert "COM3" in capture.notes_path.read_text(encoding="utf-8")


def test_sniffer_can_show_help_without_importing_pyserial(monkeypatch, capsys):
    import sniff

    monkeypatch.setattr(sys, "argv", ["sniff.py", "--help"])

    try:
        sniff.parse_args()
    except SystemExit as error:
        assert error.code == 0

    assert "Never transmits" in capsys.readouterr().out
