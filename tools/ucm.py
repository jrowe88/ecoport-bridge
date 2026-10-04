#!/usr/bin/env python3
"""Act as a minimal CTA-2045 UCM on the RS-485 bus. THIS TOOL TRANSMITS.

What it sends (and nothing else):
* Link-layer ACK/NAK replies to every packet the appliance sends.
* A Maximum Payload Length response when the appliance asks.
* With --probe: Message Type Supported Queries, a max payload query,
  "Outside comm status: good", "Query operational state" and the read-only
  Intermediate DR GetInformation and Commodity Read. Then status, operational
  state and Commodity Read every --keepalive seconds.
* With --survey: also one pass of every spec-defined read (SURVEY_SEQUENCE).

Without --allow-control it never sends shed, load-up, setpoint, price or other
control commands. With it, the operator can type Basic DR commands (/shed 7.5,
/loadup 15, /end, ...); End Shed is always sent on exit.
While it runs, type a note and press Enter to log a timestamped MARK line.

Usage:
    python tools/ucm.py --port COM4 --scenario first-ack --transmit --probe
"""

from __future__ import annotations

import argparse
import queue
import random
import sys
import threading
import time
from collections import deque
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python" / "src"))

from ecoport.cta2045.framing import Frame, StreamParser
from ecoport.cta2045.ucm import (
    END_SHED,
    KEEPALIVE_SEQUENCE,
    OPSTATE_QUERY,
    PROBE_SEQUENCE,
    SURVEY_SEQUENCE,
    UcmResponder,
    parse_control,
)
from packet_capture import CaptureWriter

DEFAULT_OUTPUT_ROOT = Path(__file__).resolve().parents[1] / "captures" / "rinnai"

LINK_REPLY_DELAY = 0.06  # tMA: 40-200 ms after the end of a message
APP_REPLY_DELAY = 1.0  # tAR >= 100 ms (tAAR <= 3 s); Rinnai drops replies sent ~150 ms after our ACK
ACK_TIMEOUT = 0.30  # tMA max (200 ms) plus USB latency margin
BUS_IDLE = 0.04  # don't start talking within 40 ms of the last received byte
STALE_PARTIAL = 0.50  # tML: a message must complete within 500 ms
PROBE_GAP = 1.0
SGD_QUIET = 0.6  # Rinnai discovery rounds send 4 frames ~300 ms apart; don't start a request inside one
ECHO_WINDOW = 0.05
MAX_RETRIES = 3  # §6.1.5.2


@dataclass
class Outgoing:
    due: float
    label: str
    data: bytes
    needs_ack: bool
    attempt: int = 0
    reply: bool = False


class UcmSession:
    """Serial event loop. ``port`` needs read/write/in_waiting; ``clock`` returns seconds."""

    def __init__(
        self,
        port: Any,
        capture: CaptureWriter | None,
        responder: UcmResponder,
        probe: bool,
        keepalive: float,
        log=print,
        clock=time.monotonic,
        rts_tx: bool = False,
        app_reply_delay: float = APP_REPLY_DELAY,
        echo_filter: bool = False,
    ) -> None:
        self.port = port
        self.capture = capture
        self.responder = responder
        self.echo_filter = echo_filter
        self.keepalive = keepalive if probe else 0
        self.log = log
        self.clock = clock
        self.rts_tx = rts_tx
        self.app_reply_delay = app_reply_delay
        self.parser = StreamParser()
        self.link_replies: list[Outgoing] = []
        self.messages: deque[Outgoing] = deque()
        self.awaiting: Outgoing | None = None
        self.awaiting_since = 0.0
        self.echo = bytearray()
        self.echo_deadline = 0.0
        self.last_rx = -1.0
        self.next_message_at = 0.0
        self.sgd_quiet_until = 0.0
        self.next_keepalive = 0.0
        self.stats = {"rx_packets": 0, "tx_packets": 0, "acked": 0, "unanswered": 0}
        if probe:
            for label, data in PROBE_SEQUENCE:
                self.queue(label, data)
            self.next_keepalive = clock() + keepalive if keepalive else 0.0

    def queue(self, label: str, data: bytes) -> None:
        self.messages.append(Outgoing(0.0, label, data, needs_ack=True))

    def send_next(self, label: str, data: bytes) -> None:
        """Put a message at the front of the queue (still waits for any in-flight ACK)."""
        self.messages.appendleft(Outgoing(0.0, label, data, needs_ack=True))

    def idle(self) -> bool:
        return self.awaiting is None and not self.messages and not self.link_replies

    def step(self) -> None:
        now = self.clock()
        data = self.port.read(self.port.in_waiting or 1)
        if data:
            self._receive(data, now)
        elif self.parser.pending and now - self.last_rx > STALE_PARTIAL:
            self.log(f"   drop partial {self.parser.flush_stale().hex(' ')}")
        if self.echo and now > self.echo_deadline:
            self.echo.clear()
        self._check_ack_timeout(now)
        if self.keepalive and now >= self.next_keepalive:
            self.next_keepalive = now + self.keepalive
            for label, frame_bytes in KEEPALIVE_SEQUENCE:
                self.queue(label, frame_bytes)
        self._send_due(now)

    def _receive(self, data: bytes, now: float) -> None:
        stamp = datetime.now().astimezone()
        if self.echo:
            n = 0
            while n < len(data) and n < len(self.echo) and data[n] == self.echo[n]:
                n += 1
            if n:
                if self.capture:
                    self.capture.record_tx(data[:n], stamp, "echo", "echo")
                del self.echo[:n]
                data = data[n:]
        if not data:
            return
        self.last_rx = now
        if self.capture:
            self.capture.record(data, stamp)
        for packet in self.parser.feed(data):
            self._handle(packet, now)

    def _handle(self, packet: Frame, now: float) -> None:
        self.stats["rx_packets"] += 1
        reaction = self.responder.react(packet)
        self.log(f"RX {packet.raw.hex(' '):<28} {reaction.note}")
        if packet.is_link_control:
            if self.awaiting is not None:
                if packet.is_link_ack:
                    self.stats["acked"] += 1
                self.awaiting = None
                self.next_message_at = now + PROBE_GAP
            return
        if reaction.link_reply:
            self.sgd_quiet_until = now + SGD_QUIET
            self.link_replies.append(
                Outgoing(now + LINK_REPLY_DELAY, "link reply", reaction.link_reply, False)
            )
        for i, (label, data) in enumerate(reaction.app_replies):
            # A repeated query supersedes any unsent reply to the previous one
            # (the Rinnai NAKs duplicate max-payload responses with 15 07).
            self.messages = deque(m for m in self.messages if m.data != data)
            # Replies jump the probe queue but still wait for their own link ACK.
            self.messages.insert(i, Outgoing(0.0, label, data, needs_ack=True, reply=True))
        if reaction.app_replies:
            self.next_message_at = max(
                self.next_message_at, now + LINK_REPLY_DELAY + self.app_reply_delay
            )

    def _check_ack_timeout(self, now: float) -> None:
        if self.awaiting is None or now - self.awaiting_since < ACK_TIMEOUT:
            return
        pending = self.awaiting
        self.awaiting = None
        if pending.attempt < MAX_RETRIES:
            pending.attempt += 1
            self.log(f"   no ACK for {pending.label}; retry {pending.attempt}/{MAX_RETRIES}")
            self.messages.appendleft(pending)
            self.next_message_at = now + random.uniform(0.1, 2.0)
        else:
            self.stats["unanswered"] += 1
            self.log(f"   no ACK for {pending.label}; giving up")
            self.next_message_at = now + PROBE_GAP

    def _bus_idle(self, now: float) -> bool:
        return not self.parser.pending and now - self.last_rx >= BUS_IDLE

    def _send_due(self, now: float) -> None:
        if self.link_replies and self.link_replies[0].due <= now:
            reply = self.link_replies.pop(0)
            self._write(reply)
            return
        if (
            self.awaiting is None
            and not self.link_replies
            and self.messages
            and now >= self.next_message_at
            and (self.messages[0].reply or now >= self.sgd_quiet_until)
            and self._bus_idle(now)
        ):
            message = self.messages.popleft()
            self._write(message)
            self.awaiting = message
            self.awaiting_since = self.clock()

    def _write(self, item: Outgoing) -> None:
        if self.rts_tx:
            self.port.rts = True
        self.port.write(item.data)
        self.port.flush()
        if self.rts_tx:
            self.port.rts = False
        self.stats["tx_packets"] += 1
        if self.echo_filter:
            # A real echo arrives within a few ms. Waiting longer would swallow the
            # SGD's reply headers, which often start with the same bytes as our TX.
            self.echo.extend(item.data)
            self.echo_deadline = self.clock() + ECHO_WINDOW
        if self.capture:
            self.capture.record_tx(item.data, datetime.now().astimezone(), item.label)
        retry = f" (retry {item.attempt})" if item.attempt else ""
        self.log(f"TX {item.data.hex(' '):<28} {item.label}{retry}")


def open_serial(port: str) -> Any:
    try:
        import serial
    except ImportError as error:
        raise SystemExit("pyserial is required. Install it from python/: pip install -e .") from error
    # serial_for_url also accepts "loop://" for a hardware-free smoke test.
    connection = serial.serial_for_url(port, do_not_open=True)
    connection.baudrate = 19_200
    connection.bytesize = serial.EIGHTBITS
    connection.parity = serial.PARITY_NONE
    connection.stopbits = serial.STOPBITS_ONE
    connection.timeout = 0.01
    connection.write_timeout = 0.5
    connection.dtr = False
    connection.rts = False
    try:
        connection.open()
    except serial.SerialException as error:
        raise SystemExit(f"Could not open {port}: {error}") from error
    return connection


def _indicator(value: str) -> int | None:
    return None if value.lower() == "nak" else int(value, 0)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Minimal CTA-2045 UCM. TRANSMITS on the bus.")
    parser.add_argument("--port", required=True)
    parser.add_argument("--scenario", default="ucm-session",
                        help="Name used for the capture directory (default: ucm-session).")
    parser.add_argument("--transmit", action="store_true",
                        help="Required acknowledgement that this tool writes to the bus.")
    parser.add_argument("--probe", action="store_true",
                        help="Start the handshake ourselves and send read-only queries.")
    parser.add_argument("--survey", action="store_true",
                        help="Implies --probe. Then send each spec-defined read once (CTA-2045-B "
                             "§8.2/§11); no vendor-proprietary types.")
    parser.add_argument("--keepalive", type=float, default=60, metavar="SECONDS",
                        help="With --probe, resend status + opstate query this often (0 = off).")
    parser.add_argument("--max-payload-indicator", type=_indicator, default=0x07,
                        help="Our Max Payload Length response value (default 0x07 = 256 bytes, the "
                             "CTA-2045-B Level 2 minimum; 'nak' = link NAK, i.e. default 2 bytes only).")
    parser.add_argument("--app-reply-delay", type=float, default=APP_REPLY_DELAY, metavar="SECONDS",
                        help=f"Wait after our link ACK before an application reply (default {APP_REPLY_DELAY}).")
    parser.add_argument("--rts-tx", action="store_true",
                        help="Raise RTS while transmitting (only for adapters without auto-direction).")
    parser.add_argument("--echo-filter", action="store_true",
                        help="Strip our own transmitted bytes if the adapter echoes them (FTDI on COM4 does not).")
    parser.add_argument("--allow-control", action="store_true",
                        help="Accept typed Basic DR commands (/shed, /cpe, /ge, /loadup, /power, /end). "
                             "End Shed is sent on exit.")
    parser.add_argument("--duration", type=float, default=0, metavar="SECONDS")
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    return parser.parse_args(argv)


def start_mark_reader() -> queue.Queue[str]:
    """Read operator notes from stdin on a daemon thread so the serial loop never blocks."""
    marks: queue.Queue[str] = queue.Queue()

    def reader() -> None:
        for line in sys.stdin:
            if line.strip():
                marks.put(line.strip())

    threading.Thread(target=reader, daemon=True).start()
    return marks


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if not args.transmit:
        raise SystemExit("Refusing to run without --transmit (this tool writes to the RS-485 bus).")
    args.probe = args.probe or args.survey

    metadata = {
        "source": "Rinnai REHP65 CTA-2045 active UCM session",
        "serial_port": args.port,
        "serial_settings": "19200 baud, 8 data bits, no parity, 1 stop bit",
        "transmit_policy": (
            "TRANSMITS: link ACK/NAK, max payload response"
            + (", discovery, max payload query, outside-comm-good, opstate query, "
               "GetInformation" if args.probe else "")
            + (", survey of spec-defined reads (supported queries 08 04 / 09 01-0C, "
               "Intermediate Get* requests)" if args.survey else "")
            + ("; Basic DR control commands typed by the operator (/shed, /cpe, /ge, /loadup, "
               "/power, /end) with End Shed on exit" if args.allow_control
               else "; never control commands")
            + "; never vendor-proprietary types. TX log in transmit.jsonl"
        ),
        "scenario": args.scenario,
    }
    connection = open_serial(args.port)
    started = time.monotonic()
    with connection, CaptureWriter(args.output_root, args.scenario, metadata) as capture:
        session_log = (capture.directory / "session.log").open("w", encoding="utf-8")

        def log(line: str) -> None:
            stamped = f"{datetime.now().astimezone():%H:%M:%S.%f}"[:-3] + f" {line}"
            print(stamped)
            session_log.write(stamped + "\n")
            session_log.flush()

        session = UcmSession(
            connection, capture, UcmResponder(args.max_payload_indicator),
            args.probe, args.keepalive, log=log, rts_tx=args.rts_tx,
            app_reply_delay=args.app_reply_delay, echo_filter=args.echo_filter,
        )
        if args.survey:
            for label, data in SURVEY_SEQUENCE:
                session.queue(label, data)
        log(f"UCM on {args.port}; writing to {capture.directory}. Ctrl+C to stop.")
        log("Type a note and press Enter to timestamp an operator action (logged as MARK).")
        if args.allow_control:
            log("CONTROL ENABLED: /shed [min], /cpe [min], /ge [min], /loadup [min], /power <0-100>, /end. "
                "End Shed is sent automatically on exit.")
        marks = start_mark_reader()
        control_sent = False
        try:
            while args.duration == 0 or time.monotonic() - started < args.duration:
                session.step()
                while not marks.empty():
                    control_sent |= operator_input(session, marks.get_nowait(), args.allow_control, log)
        except KeyboardInterrupt:
            log("Stopped by operator.")
        if control_sent:
            end_control(session, log)
        log(f"Stats: {session.stats}")
        session_log.close()
    return 0


def operator_input(session: UcmSession, text: str, allow_control: bool, log) -> bool:
    """Log an operator line; ``/command`` lines queue a Basic DR command. Returns True if one was queued."""
    log(f"MARK {text}")
    if not text.startswith("/"):
        return False
    if not allow_control:
        log("   control commands need --allow-control; nothing sent")
        return False
    try:
        label, data = parse_control(text)
    except ValueError as error:
        log(f"   {error}; nothing sent")
        return False
    # Command first, then confirm its effect with an opstate query (§10: ACK != state change).
    session.send_next("Query operational state", OPSTATE_QUERY)
    session.send_next(label, data)
    log(f"   queued {label}: {data.hex(' ')}")
    return True


def end_control(session: UcmSession, log, timeout: float = 10.0, tick=None) -> None:
    """Send End Shed / Run Normal and wait for it to be acknowledged (or give up after ``timeout``)."""
    log("Sending End Shed before exit.")
    session.send_next("End Shed / Run Normal (exit)", END_SHED)
    deadline = session.clock() + timeout
    try:
        while session.clock() < deadline and not session.idle():
            session.step()
            if tick:
                tick()
    except KeyboardInterrupt:
        log("   interrupted; the heater will still revert when the event duration expires")
        return
    sent = not any(m.data == END_SHED for m in session.messages)
    log("   End Shed sent" if sent else "   End Shed NOT sent; event will expire on its own")


if __name__ == "__main__":
    sys.exit(main())
