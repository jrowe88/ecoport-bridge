"""Tests for the minimal UCM responder and the serial session loop (no hardware)."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))

from ecoport.cta2045.framing import LINK_ACK, Frame, StreamParser, frame, split_frames
from ecoport.cta2045.ucm import (
    GET_INFORMATION,
    OPSTATE_QUERY,
    OUTSIDE_COMM_GOOD,
    UcmResponder,
)
from ucm import UcmSession

RINNAI_DISCOVERY = [
    "08 01 00 00 7e cd",
    "08 02 00 00 7a d0",
    "08 03 00 00 76 d3",
    "08 03 00 02 18 00 ba 75",
]


def react(hex_text):
    return UcmResponder().react(Frame(0, bytes.fromhex(hex_text)))


def test_acks_every_rinnai_discovery_frame():
    for text in RINNAI_DISCOVERY:
        assert react(text).link_reply == LINK_ACK


def test_max_payload_query_gets_ack_then_response():
    reaction = react("08 03 00 02 18 00 ba 75")
    assert reaction.app_replies == [("Max payload response", frame(b"\x08\x03", b"\x19\x06"))]


def test_naks_unsupported_type_and_other_data_link_requests():
    assert react(frame(b"\x08\x04").hex()).link_reply == b"\x15\x06"
    assert react("08 03 00 02 17 01" + frame(b"\x08\x03", b"\x17\x01")[-2:].hex()).link_reply == b"\x15\x07"


def test_does_not_ack_an_ack_and_decodes_opstate():
    assert react("06 00").link_reply is None
    reaction = react(frame(b"\x08\x01", b"\x13\x00").hex())
    assert reaction.link_reply == LINK_ACK
    assert reaction.app_replies == []
    assert "Idle Normal" in reaction.note


def test_stream_parser_handles_split_reads_ack_and_junk():
    parser = StreamParser()
    stream = b"\xff" + bytes.fromhex(RINNAI_DISCOVERY[3]) + LINK_ACK
    packets = [p for i in range(len(stream)) for p in parser.feed(stream[i : i + 1])]
    assert [p.raw for p in packets] == [bytes.fromhex(RINNAI_DISCOVERY[3]), LINK_ACK]
    assert bytes(parser.dropped) == b"\xff"


def test_split_frames_recognises_link_ack_and_nak():
    frames, junk = split_frames(LINK_ACK + bytes.fromhex(RINNAI_DISCOVERY[0]) + b"\x15\x06")
    assert [f.raw.hex(" ") for f in frames] == ["06 00", RINNAI_DISCOVERY[0], "15 06"]
    assert junk == []


class Clock:
    def __init__(self):
        self.t = 0.0

    def __call__(self):
        return self.t


class FakeSgd:
    """Serial port stand-in: echoes our bytes (like a 2-wire adapter) and plays an SGD."""

    def __init__(self, clock, ack=True):
        self.clock = clock
        self.ack = ack
        self.inbox = bytearray()
        self.scheduled = []  # (due, bytes)
        self.received = []
        self.parser = StreamParser()
        self.rts = False

    @property
    def in_waiting(self):
        self._deliver()
        return len(self.inbox)

    def _deliver(self):
        for item in [s for s in self.scheduled if s[0] <= self.clock.t]:
            self.inbox.extend(item[1])
            self.scheduled.remove(item)

    def read(self, n):
        self._deliver()
        data = bytes(self.inbox[:n])
        del self.inbox[:n]
        return data

    def flush(self):
        pass

    def write(self, data):
        self.inbox.extend(data)  # echo
        for packet in self.parser.feed(data):
            self.received.append(packet.raw)
            if packet.is_link_control or not self.ack:
                continue
            self.scheduled.append((self.clock.t + 0.05, LINK_ACK))
            if packet.raw == OPSTATE_QUERY:
                self.scheduled.append((self.clock.t + 0.3, frame(b"\x08\x01", b"\x13\x00")))

    def sgd_sends(self, data, delay=0.0):
        self.scheduled.append((self.clock.t + delay, data))


def run(session, clock, seconds):
    end = clock.t + seconds
    while clock.t < end:
        session.step()
        clock.t += 0.01


def test_session_acks_rinnai_discovery_with_correct_timing():
    clock = Clock()
    port = FakeSgd(clock)
    lines = []
    session = UcmSession(port, None, UcmResponder(), probe=False, keepalive=0, log=lines.append, clock=clock)
    port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[0]))
    port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[3]), delay=1.0)
    run(session, clock, 3)
    assert port.received == [LINK_ACK, LINK_ACK, frame(b"\x08\x03", b"\x19\x06")]
    assert session.stats["acked"] == 1
    assert not any("drop partial" in line for line in lines)


def test_probe_sequence_runs_and_reads_operational_state():
    clock = Clock()
    port = FakeSgd(clock)
    lines = []
    session = UcmSession(port, None, UcmResponder(), probe=True, keepalive=0, log=lines.append, clock=clock)
    run(session, clock, 15)
    sent = [r for r in port.received if r != LINK_ACK]
    assert OUTSIDE_COMM_GOOD in sent and OPSTATE_QUERY in sent and GET_INFORMATION in sent
    assert any("operational state 0 (Idle Normal)" in line for line in lines)
    assert session.stats["unanswered"] == 0


def test_probe_retries_three_times_then_gives_up_when_silent():
    clock = Clock()
    port = FakeSgd(clock, ack=False)
    session = UcmSession(port, None, UcmResponder(), probe=True, keepalive=0, log=lambda _: None, clock=clock)
    run(session, clock, 12)
    assert port.received[:4] == [frame(b"\x08\x01")] * 4
    assert session.stats["unanswered"] >= 1
