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
MAX_PAYLOAD_QUERY = frame(DATA_LINK, bytes((DL_MAX_PAYLOAD_QUERY, 0x00)))
GET_INFORMATION = frame(INTERMEDIATE_DR, b"\x01\x01")
# Get variants are exactly 2 bytes; the Set variants share opcodes but are longer (§11.1.6).
GET_SETPOINT = frame(INTERMEDIATE_DR, b"\x03\x03")
GET_PRESENT_TEMPERATURE = frame(INTERMEDIATE_DR, b"\x03\x04")

# Read-only startup probe, sent by the UCM in this order.
PROBE_SEQUENCE: tuple[tuple[str, bytes], ...] = (
    ("Supported? Basic DR", supported_query(BASIC_DR)),
    ("Supported? Intermediate DR", supported_query(INTERMEDIATE_DR)),
    ("Supported? Data-Link", supported_query(DATA_LINK)),
    ("Query SGD max payload", MAX_PAYLOAD_QUERY),
    ("Outside comm status: good", OUTSIDE_COMM_GOOD),
    ("Query operational state", OPSTATE_QUERY),
    ("GetInformation", GET_INFORMATION),
)
KEEPALIVE_SEQUENCE: tuple[tuple[str, bytes], ...] = (
    ("Outside comm status: good", OUTSIDE_COMM_GOOD),
    ("Query operational state", OPSTATE_QUERY),
)


def _intermediate(label: str, payload_hex: str) -> tuple[str, bytes]:
    return label, frame(INTERMEDIATE_DR, bytes.fromhex(payload_hex))


# One pass over every spec-defined *read* (CTA-2045-B §8.2, §11). Each request
# was checked against the spec: its Set counterpart either uses a different
# Opcode2 or needs a longer payload, so none of these can change device state.
# Vendor-proprietary message types and opcodes (0xF0-0xFF) are deliberately excluded.
SURVEY_SEQUENCE: tuple[tuple[str, bytes], ...] = (
    ("Supported? Commissioning", supported_query(b"\x08\x04")),
    *((f"Supported? pass-through 09 {n:02X}", supported_query(bytes((0x09, n)))) for n in range(0x01, 0x0D)),
    _intermediate("GetSGDEfficiencyLevel", "01 02"),
    _intermediate("GetUTCTime", "02 00"),
    _intermediate("GetEnergyPrice", "03 00"),
    _intermediate("GetTier", "03 01"),
    _intermediate("GetTemperatureOffset", "03 02"),
    _intermediate("GetSetPoint", "03 03"),
    _intermediate("GetPresentTemperature", "03 04"),
    _intermediate("GetCommodityRead (all)", "06 00"),
    _intermediate("GetCommoditySubscription", "06 01"),
    _intermediate("GetActivationStatus index 0", "0A 00 00"),
    _intermediate("GetPreferenceLevel demand", "0B 00 00"),
    _intermediate("GetPreferenceLevel energy", "0B 00 01"),
)

RESPONSE_CODES = {
    0x00: "success",
    0x01: "command not implemented",
    0x02: "bad value",
    0x03: "command length error",
    0x04: "response length error",
    0x05: "busy",
    0x06: "other error",
    0x07: "customer override in effect",
    0x08: "command not enabled",
}
UNITS = {0: "F", 1: "C"}
NOT_SUPPORTED_TEMP = -0x8000


def describe_intermediate(payload: bytes) -> str:
    """Human-readable summary of an Intermediate DR payload we know how to read."""
    # Activation (0A) and preference (0B) replies have no response-code byte.
    if len(payload) >= 3 and payload[1] & 0x80 and payload[0] not in (0x0A, 0x0B):
        code = payload[2]
        if code != 0x00:
            reason = RESPONSE_CODES.get(code, "reserved")
            return f"intermediate reply {payload[0]:02X} {payload[1]:02X}: code 0x{code:02X} ({reason})"
    decoder = REPLY_DECODERS.get(payload[:2])
    if decoder is not None:
        name, parse, minimum = decoder
        if len(payload) >= minimum:
            return f"{name} reply: " + _fmt(parse(payload))
    return f"intermediate DR payload {payload.hex(' ')}"


def _fmt(values: dict[str, object]) -> str:
    return ", ".join(f"{k}={v}" for k, v in values.items())


def _temp(raw: bytes, scale: int = 1) -> float | int | None:
    value = int.from_bytes(raw, "big", signed=True)
    if value == NOT_SUPPORTED_TEMP:
        return None
    return value / scale if scale != 1 else value


def parse_setpoint(payload: bytes) -> dict[str, object]:
    """Decode a GetSetPoint() reply (§11.1.6.2). Set points are whole degrees."""
    units = UNITS.get(payload[5], f"0x{payload[5]:02X}")
    values: dict[str, object] = {
        "device_type": f"0x{int.from_bytes(payload[3:5], 'big'):04X}",
        "units": units,
        "setpoint1": _temp(payload[6:8]),
    }
    if len(payload) >= 10:
        values["setpoint2"] = _temp(payload[8:10])
    return values


def parse_present_temperature(payload: bytes) -> dict[str, object]:
    """Decode a GetPresentTemperature() reply (§11.1.7.2), reported in 1/100 degree."""
    return {
        "device_type": f"0x{int.from_bytes(payload[3:5], 'big'):04X}",
        "units": UNITS.get(payload[5], f"0x{payload[5]:02X}"),
        "temperature": _temp(payload[6:8], scale=100),
    }


COMMODITY_CODES = {
    0: "electricity consumed (W, Wh)",
    1: "electricity produced (W, Wh)",
    2: "natural gas (cu-ft/h, cu-ft)",
    3: "water (gal/h, gal)",
    4: "natural gas (m3/h, m3)",
    5: "water (l/h, l)",
    6: "total energy storage capacity (Wh)",
    7: "present energy storage capacity (Wh)",
    8: "rated max consumption (W)",
    9: "rated max production (W)",
    10: "advanced load up total capacity (Wh)",
    11: "advanced load up present capacity (Wh)",
}
NOT_SUPPORTED_48 = 0xFFFF_FFFF_FFFF


def _u48(raw: bytes) -> int | None:
    value = int.from_bytes(raw, "big")
    return None if value == NOT_SUPPORTED_48 else value


def parse_commodity_read(payload: bytes) -> dict[str, object]:
    """Decode a Get CommodityRead reply (§11.3.1.2): 13-byte records after the response code."""
    values: dict[str, object] = {}
    for start in range(3, len(payload) - 12, 13):
        code = payload[start]
        name = COMMODITY_CODES.get(code & 0x7F, f"code {code & 0x7F}")
        source = "measured" if code & 0x80 else "estimated"
        rate, total = _u48(payload[start + 1 : start + 7]), _u48(payload[start + 7 : start + 13])
        values[name] = f"rate={rate} cumulative={total} ({source})"
    return values


def parse_commodity_subscription(payload: bytes) -> dict[str, object]:
    """Decode a GetCommoditySubscription reply (§11.3.2.2): (type, update seconds) pairs."""
    return {
        COMMODITY_CODES.get(payload[i] & 0x7F, f"code {payload[i]}"): f"every {int.from_bytes(payload[i + 1 : i + 3], 'big')} s"
        for i in range(3, len(payload) - 2, 3)
    }


def parse_utc_time(payload: bytes) -> dict[str, object]:
    """Decode a GetUTCTime reply (§11.1.2.2): seconds since 2000-01-01 UTC."""
    from datetime import datetime, timedelta, timezone

    seconds = int.from_bytes(payload[3:7], "big")
    when = datetime(2000, 1, 1, tzinfo=timezone.utc) + timedelta(seconds=seconds)
    return {
        "utc": when.isoformat(),
        "tz_offset_h": int.from_bytes(payload[7:8], "big", signed=True) / 4,
        "dst_offset_h": payload[8] / 4,
    }


REPLY_DECODERS = {
    b"\x01\x81": ("GetInformation", lambda p: parse_device_info(p), 15),
    b"\x01\x82": ("GetSGDEfficiencyLevel", lambda p: {"level": p[3]}, 4),
    b"\x02\x80": ("GetUTCTime", parse_utc_time, 9),
    b"\x03\x81": ("GetTier", lambda p: {"tier": p[3]}, 4),
    b"\x03\x82": ("GetTemperatureOffset", lambda p: {"offset": p[3], "units": UNITS.get(p[4], p[4])}, 5),
    b"\x03\x83": ("GetSetPoint", parse_setpoint, 8),
    b"\x03\x84": ("GetPresentTemperature", parse_present_temperature, 8),
    b"\x06\x80": ("GetCommodityRead", parse_commodity_read, 16),
    b"\x06\x81": ("GetCommoditySubscription", parse_commodity_subscription, 6),
    b"\x0a\x80": ("GetActivationStatus", lambda p: {"index": p[2], "status": p[3]}, 4),
    b"\x0b\x80": ("GetPreferenceLevel", lambda p: {"type": p[2], "level": p[3]}, 4),
}


def parse_device_info(payload: bytes) -> dict[str, object]:
    """Decode a GetInformation() reply payload (CTA-2045-B §11.1.1.2)."""
    info: dict[str, object] = {
        "response_code": payload[2],
        "cta2045_version": payload[3:5].split(b"\x00")[0].decode("ascii", "replace"),
        "vendor_id": f"0x{int.from_bytes(payload[5:7], 'big'):04X}",
        "device_type": f"0x{int.from_bytes(payload[7:9], 'big'):04X}",
        "device_revision": int.from_bytes(payload[9:11], "big"),
        "capability_bitmap": f"0x{int.from_bytes(payload[11:15], 'big'):08X}",
    }
    if len(payload) >= 32:
        info["model"] = payload[16:32].split(b"\x00")[0].decode("ascii", "replace").strip()
    if len(payload) >= 48:
        info["serial"] = payload[32:48].split(b"\x00")[0].decode("ascii", "replace").strip()
    if len(payload) >= 53:
        y, m, d, major, minor = payload[48:53]
        info["firmware"] = f"20{y:02d}-{m:02d}-{d:02d} v{major}.{minor}"
    return info


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
    # 0x07 = 256 bytes, the Level 2 minimum (§22.4.1.1.4); None = link NAK (2-byte default)
    max_payload_indicator: int | None = 0x07

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
        return Reaction(LINK_ACK, note=describe_intermediate(payload))

    def _data_link(self, payload: bytes) -> Reaction:
        opcode = payload[0]
        if opcode == DL_MAX_PAYLOAD_QUERY:
            if self.max_payload_indicator is None:
                return Reaction(
                    link_nak(NAK_REQUEST_NOT_SUPPORTED),
                    note="max payload query (NAK: default 2 bytes only)",
                )
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
