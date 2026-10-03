"""Minimal, conservative CTA-2045 UCM (communication module) behaviour.

Pure protocol logic with no serial I/O, so it can be unit tested. It only
answers the SGD and sends discovery, connection-status and read-only
queries. It never sends curtailment, load-up, setpoint or similar commands.
References are to ANSI/CTA-2045-B.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from .framing import LINK_ACK, Frame, frame

BASIC_DR = b"\x08\x01"
INTERMEDIATE_DR = b"\x08\x02"
DATA_LINK = b"\x08\x03"
SUPPORTED_TYPES = (BASIC_DR, INTERMEDIATE_DR, DATA_LINK)

NAK_CHECKSUM = 0x03
NAK_UNSUPPORTED_TYPE = 0x06
NAK_REQUEST_NOT_SUPPORTED = 0x07

DL_MAX_PAYLOAD_QUERY = 0x18
DL_MAX_PAYLOAD_RESPONSE = 0x19

BASIC_APP_ACK = 0x03
BASIC_APP_NAK = 0x04
BASIC_OUTSIDE_COMM_STATUS = 0x0E
BASIC_CUSTOMER_OVERRIDE = 0x11
BASIC_OPSTATE_QUERY = 0x12
BASIC_OPSTATE_RESPONSE = 0x13
BASIC_SLEEP = 0x14
BASIC_WAKE = 0x15
# SGD-to-UCM notifications we acknowledge at the application layer (§10).
BASIC_SGD_NOTIFICATIONS = (BASIC_CUSTOMER_OVERRIDE, BASIC_SLEEP, BASIC_WAKE)

OPERATIONAL_STATES = {
    0: "Idle Normal",
    1: "Running Normal",
    2: "Running Curtailed",
    3: "Running Heightened",
    4: "Idle Curtailed",
    5: "SGD Error Condition",
    6: "Idle Heightened",
    7: "Cycling On",
    8: "Cycling Off",
    9: "Variable Following",
    10: "Variable Not Following",
    11: "Idle, Opted Out",
    12: "Running, Opted Out",
    13: "Running, Price Stream",
    14: "Idle, Price Stream",
}


def link_nak(code: int) -> bytes:
    return bytes((0x15, code))


def basic(opcode1: int, opcode2: int = 0) -> bytes:
    return frame(BASIC_DR, bytes((opcode1, opcode2)))


def supported_query(msg_type: bytes) -> bytes:
    return frame(msg_type)


OUTSIDE_COMM_GOOD = basic(BASIC_OUTSIDE_COMM_STATUS, 0x01)
OPSTATE_QUERY = basic(BASIC_OPSTATE_QUERY)
GET_INFORMATION = frame(INTERMEDIATE_DR, b"\x01\x01")

# Read-only startup probe, sent by the UCM in this order.
PROBE_SEQUENCE: tuple[tuple[str, bytes], ...] = (
    ("Supported? Basic DR", supported_query(BASIC_DR)),
    ("Supported? Intermediate DR", supported_query(INTERMEDIATE_DR)),
    ("Supported? Data-Link", supported_query(DATA_LINK)),
    ("Outside comm status: good", OUTSIDE_COMM_GOOD),
    ("Query operational state", OPSTATE_QUERY),
    ("GetInformation", GET_INFORMATION),
)
KEEPALIVE_SEQUENCE: tuple[tuple[str, bytes], ...] = (
    ("Outside comm status: good", OUTSIDE_COMM_GOOD),
    ("Query operational state", OPSTATE_QUERY),
)


@dataclass
class Reaction:
    """What the UCM should do in response to one received packet."""

    link_reply: bytes | None = None
    """Sent 40-200 ms after the received packet (tMA)."""
    app_replies: list[tuple[str, bytes]] = field(default_factory=list)
    """Sent after our link reply has gone out (tAR >= 100 ms)."""
    note: str = ""


@dataclass
class UcmResponder:
    max_payload_indicator: int = 0x06  # 128 bytes (§9 Table 9-2)

    def react(self, packet: Frame) -> Reaction:
        if packet.is_link_ack:
            return Reaction(note="link ACK")
        if packet.is_link_nak:
            return Reaction(note=f"link NAK code 0x{packet.raw[1]:02X}")
        if not packet.checksum_ok:
            return Reaction(link_nak(NAK_CHECKSUM), note="bad checksum")

        msg_type, payload = packet.msg_type, packet.payload
        if msg_type not in SUPPORTED_TYPES:
            return Reaction(link_nak(NAK_UNSUPPORTED_TYPE), note="unsupported message type")
        if not payload:
            return Reaction(LINK_ACK, note=f"supported query {msg_type.hex(' ')}")
        if msg_type == DATA_LINK:
            return self._data_link(payload)
        if msg_type == BASIC_DR:
            return self._basic(payload)
        return Reaction(LINK_ACK, note=f"intermediate DR payload {payload.hex(' ')}")

    def _data_link(self, payload: bytes) -> Reaction:
        opcode = payload[0]
        if opcode == DL_MAX_PAYLOAD_QUERY:
            response = frame(DATA_LINK, bytes((DL_MAX_PAYLOAD_RESPONSE, self.max_payload_indicator)))
            return Reaction(
                LINK_ACK,
                [("Max payload response", response)],
                note="max payload query",
            )
        if opcode == DL_MAX_PAYLOAD_RESPONSE:
            return Reaction(LINK_ACK, note=f"SGD max payload indicator 0x{payload[1]:02X}")
        # Power mode, bit rate, slot number etc.: keep defaults (§9.1).
        return Reaction(link_nak(NAK_REQUEST_NOT_SUPPORTED), note=f"data-link opcode 0x{opcode:02X}")

    def _basic(self, payload: bytes) -> Reaction:
        if len(payload) != 2:
            return Reaction(LINK_ACK, [("App NAK length", basic(BASIC_APP_NAK, 0x04))])
        opcode1, opcode2 = payload
        if opcode1 == BASIC_OPSTATE_RESPONSE:
            name = OPERATIONAL_STATES.get(opcode2, "reserved/unknown")
            return Reaction(LINK_ACK, note=f"operational state {opcode2} ({name})")
        if opcode1 == BASIC_APP_ACK:
            return Reaction(LINK_ACK, note=f"app ACK of opcode 0x{opcode2:02X}")
        if opcode1 == BASIC_APP_NAK:
            return Reaction(LINK_ACK, note=f"app NAK reason 0x{opcode2:02X}")
        if opcode1 in BASIC_SGD_NOTIFICATIONS:
            return Reaction(
                LINK_ACK,
                [("App ACK", basic(BASIC_APP_ACK, opcode1))],
                note=f"basic opcode 0x{opcode1:02X} value 0x{opcode2:02X}",
            )
        return Reaction(
            LINK_ACK,
            [("App NAK opcode unsupported", basic(BASIC_APP_NAK, 0x01))],
            note=f"unhandled basic opcode 0x{opcode1:02X}",
        )
