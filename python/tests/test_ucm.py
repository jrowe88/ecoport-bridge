"""Tests for the minimal UCM responder and the serial session loop (no hardware)."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))

from ecoport.cta2045.framing import LINK_ACK, Frame, StreamParser, frame, split_frames
from ecoport.cta2045.ucm import (
    GET_INFORMATION,
    GET_PRESENT_TEMPERATURE,
    GET_SETPOINT,
    MAX_PAYLOAD_QUERY,
    OPSTATE_QUERY,
    OUTSIDE_COMM_GOOD,
    SURVEY_SEQUENCE,
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
    assert reaction.app_replies == [("Max payload response", frame(b"\x08\x03", b"\x19\x07"))]


def test_max_payload_query_can_be_naked_for_default_length():
    reaction = UcmResponder(max_payload_indicator=None).react(Frame(0, bytes.fromhex("08 03 00 02 18 00 ba 75")))
    assert reaction.link_reply == b"\x15\x07"
    assert reaction.app_replies == []


def test_decodes_rinnai_get_information_reply():
    raw = bytes.fromhex("08 02 00 10 01 81 00 41 00 0c 22 00 03 00 04 00 00 00 00 00 14 2e")
    note = react(raw.hex()).note
    assert "cta2045_version=A" in note
    assert "vendor_id=0x0C22" in note
    assert "device_type=0x0003" in note
    assert "device_revision=4" in note


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
    """Serial port stand-in that plays an SGD; ``echo`` mimics adapters that echo our TX."""

    def __init__(self, clock, ack=True, echo=False, reply_delay=0.3):
        self.clock = clock
        self.ack = ack
        self.echo = echo
        self.reply_delay = reply_delay
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
        if self.echo:
            self.inbox.extend(data)
        for packet in self.parser.feed(data):
            self.received.append(packet.raw)
            if packet.is_link_control or not self.ack:
                continue
            self.scheduled.append((self.clock.t + 0.05, LINK_ACK))
            if packet.raw == OPSTATE_QUERY:
                self.scheduled.append((self.clock.t + self.reply_delay, frame(b"\x08\x01", b"\x13\x00")))

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
    sent_at = []
    session = UcmSession(port, None, UcmResponder(), probe=False, keepalive=0, log=lines.append, clock=clock)
    original_write = port.write
    port.write = lambda data: (sent_at.append((clock.t, data)), original_write(data))
    port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[0]))
    port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[3]), delay=1.0)
    run(session, clock, 4)
    assert port.received == [LINK_ACK, LINK_ACK, frame(b"\x08\x03", b"\x19\x07")]
    ack_time, response_time = sent_at[1][0], sent_at[2][0]
    assert 0.04 <= ack_time - 1.0 <= 0.2
    assert response_time - ack_time >= 0.95
    assert session.stats["acked"] == 1
    assert not any("drop partial" in line for line in lines)


def test_probe_sequence_runs_and_reads_operational_state():
    clock = Clock()
    port = FakeSgd(clock)
    lines = []
    session = UcmSession(port, None, UcmResponder(), probe=True, keepalive=0, log=lines.append, clock=clock)
    run(session, clock, 25)
    sent = [r for r in port.received if r != LINK_ACK]
    assert OUTSIDE_COMM_GOOD in sent and OPSTATE_QUERY in sent and GET_INFORMATION in sent
    assert MAX_PAYLOAD_QUERY in sent
    assert any("operational state 0 (Idle Normal)" in line for line in lines)
    assert session.stats["unanswered"] == 0


def probe_session(port, clock, lines, **kwargs):
    return UcmSession(port, None, UcmResponder(), probe=True, keepalive=0, log=lines.append, clock=clock, **kwargs)


def test_fast_reply_sharing_prefix_with_our_request_is_parsed_intact():
    # Regression: the 250 ms echo filter ate the "08 01 00 02" header of the heater's
    # opstate reply (it matches our "08 01 00 02 12 00" query) and dropped the tail.
    clock = Clock()
    port = FakeSgd(clock, reply_delay=0.2)
    lines = []
    session = probe_session(port, clock, lines)
    run(session, clock, 25)
    assert any("operational state 0 (Idle Normal)" in line for line in lines)
    assert not any("drop" in line for line in lines)


def test_requests_wait_for_heater_discovery_round_to_finish():
    # Regression (commodity-1 run): our keepalive went out between discovery
    # frames 3 and 4 (~300 ms apart), collided, and was never link-ACKed.
    clock = Clock()
    port = FakeSgd(clock)
    sent_at = []
    session = UcmSession(port, None, UcmResponder(), probe=False, keepalive=0, log=lambda _: None, clock=clock)
    original_write = port.write
    port.write = lambda data: (sent_at.append((clock.t, data)), original_write(data))
    for i, hex_frame in enumerate(RINNAI_DISCOVERY[:3]):
        port.sgd_sends(bytes.fromhex(hex_frame), delay=0.3 * i)
    port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[3]), delay=1.2)
    clock.t = 0.15
    session.queue("Outside comm status: good", OUTSIDE_COMM_GOOD)
    run(session, clock, 5)
    request_times = [t for t, data in sent_at if data == OUTSIDE_COMM_GOOD]
    assert len(request_times) == 1
    assert request_times[0] >= 1.2 + 0.6
    assert session.stats["unanswered"] == 0


def test_echo_filter_strips_adapter_echo_when_enabled():
    clock = Clock()
    port = FakeSgd(clock, echo=True)
    lines = []
    session = probe_session(port, clock, lines, echo_filter=True)
    run(session, clock, 25)
    assert any("operational state 0 (Idle Normal)" in line for line in lines)
    assert session.stats["unanswered"] == 0


def test_probe_retries_three_times_then_gives_up_when_silent():
    clock = Clock()
    port = FakeSgd(clock, ack=False)
    session = UcmSession(port, None, UcmResponder(), probe=True, keepalive=0, log=lambda _: None, clock=clock)
    run(session, clock, 12)
    assert port.received[:4] == [frame(b"\x08\x01")] * 4
    assert session.stats["unanswered"] >= 1


def intermediate(payload_hex):
    return frame(b"\x08\x02", bytes.fromhex(payload_hex)).hex()


def test_read_only_requests_are_exactly_two_byte_payloads():
    # Set variants reuse opcode 03 03 with a longer payload; never send those.
    assert GET_SETPOINT == frame(b"\x08\x02", b"\x03\x03")
    assert GET_PRESENT_TEMPERATURE == frame(b"\x08\x02", b"\x03\x04")


def test_decodes_setpoint_and_present_temperature_replies():
    assert "setpoint1=120" in react(intermediate("03 83 00 00 03 00 00 78")).note
    note = react(intermediate("03 84 00 00 03 00 2e e0")).note
    assert "temperature=120.0" in note and "units=F" in note
    assert "setpoint1=None" in react(intermediate("03 83 00 00 03 01 80 00")).note


def test_decodes_intermediate_error_response_code():
    assert "command not implemented" in react(intermediate("03 83 01")).note


def test_repeated_max_payload_queries_get_a_single_response():
    clock = Clock()
    port = FakeSgd(clock)
    session = UcmSession(port, None, UcmResponder(), probe=False, keepalive=0, log=lambda _: None, clock=clock)
    for delay in (0.0, 0.3, 0.6):
        port.sgd_sends(bytes.fromhex(RINNAI_DISCOVERY[3]), delay=delay)
    run(session, clock, 4)
    assert port.received.count(frame(b"\x08\x03", b"\x19\x07")) == 1

# Get forms verified against CTA-2045-B §11; Set forms differ (opcode2 or length).
ALLOWED_SURVEY_READS = {
    "01 02", "02 00", "03 00", "03 01", "03 02", "03 03", "03 04",
    "06 00", "06 01", "0a 00 00", "0b 00 00", "0b 00 01",
}


def test_survey_sends_only_spec_defined_reads_and_supported_queries():
    for _label, data in SURVEY_SEQUENCE:
        packet = Frame(0, data)
        assert packet.checksum_ok
        assert packet.msg_type[0] in (0x08, 0x09)  # never vendor-proprietary types
        if packet.payload:
            assert packet.msg_type == b"\x08\x02"
            assert packet.payload.hex(" ") in ALLOWED_SURVEY_READS


def test_decodes_survey_replies():
    commodity = "06 80 00 07" + "ff" * 6 + "00 00 00 00 0f a0"
    assert "present energy storage capacity (Wh)=rate=None cumulative=4000 (estimated)" in react(
        intermediate(commodity)
    ).note
    assert "status=1" in react(intermediate("0a 80 00 01")).note
    assert "level=5" in react(intermediate("0b 80 00 05")).note
    assert "utc=2000-01-01T00:01:00+00:00" in react(intermediate("02 80 00 00 00 00 3c ec 04")).note


def test_survey_runs_to_completion_when_heater_naks_everything():
    clock = Clock()
    port = FakeSgd(clock, ack=False)

    def nak_all(data):
        for packet in port.parser.feed(data):
            port.received.append(packet.raw)
            if not packet.is_link_control:
                port.scheduled.append((clock.t + 0.02, b"\x15\x07"))

    port.write = nak_all
    session = UcmSession(port, None, UcmResponder(), probe=False, keepalive=0, log=lambda _: None, clock=clock)
    for label, data in SURVEY_SEQUENCE:
        session.queue(label, data)
    run(session, clock, 40)
    assert [r for r in port.received if r != LINK_ACK] == [d for _, d in SURVEY_SEQUENCE]
    assert session.stats["unanswered"] == 0