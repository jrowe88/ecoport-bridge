#!/usr/bin/env python3
"""Passively capture raw CTA-2045 traffic from an isolated RS-485 adapter.

This tool never writes to the serial port. It is deliberately the first
hardware-facing tool for the project: capture before attempting CTA-2045 link
initialization, reads, or control commands.
"""

from __future__ import annotations

import argparse
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any

from packet_capture import CaptureWriter

DEFAULT_OUTPUT_ROOT = Path(__file__).resolve().parents[1] / "captures" / "rinnai"


class SerialPortError(RuntimeError):
    """Raised when the configured serial port cannot be opened."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Passively record raw CTA-2045 RS-485 traffic. Never transmits."
    )
    parser.add_argument(
        "--port",
        required=True,
        help="Serial port for the isolated USB-RS485 adapter (for example COM3).",
    )
    parser.add_argument(
        "--scenario",
        required=True,
        help="Short capture label, such as idle or heat-pump-only.",
    )
    parser.add_argument(
        "--duration",
        type=float,
        default=0,
        metavar="SECONDS",
        help="Stop after this many seconds. Omit or pass 0 to stop with Ctrl+C.",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=DEFAULT_OUTPUT_ROOT,
        help=f"Directory in which to create the capture (default: {DEFAULT_OUTPUT_ROOT}).",
    )
    return parser.parse_args()


def open_passive_serial(port: str) -> Any:
    """Open a serial port for receive-only capture at the CTA-2045 defaults."""
    try:
        import serial
    except ImportError as error:
        raise SystemExit(
            "pyserial is required. Install it from python/: pip install -e ."
        ) from error

    connection = serial.Serial(
        port=None,
        baudrate=19_200,
        bytesize=serial.EIGHTBITS,
        parity=serial.PARITY_NONE,
        stopbits=serial.STOPBITS_ONE,
        timeout=0.25,
        write_timeout=0.25,
        xonxoff=False,
        rtscts=False,
        dsrdtr=False,
    )
    # Keep modem-control lines inactive before opening. The tool does not use
    # serial writes or pyserial's RS-485 transmit-direction support.
    connection.dtr = False
    connection.rts = False
    connection.port = port
    try:
        connection.open()
    except serial.SerialException as error:
        raise SerialPortError(f"Could not open {port}: {error}") from error
    return connection


def main() -> int:
    args = parse_args()
    if args.duration < 0:
        raise SystemExit("--duration must be zero or a positive number")

    metadata = {
        "source": "Rinnai REHP65 CTA-2045 passive capture",
        "serial_port": args.port,
        "serial_settings": "19200 baud, 8 data bits, no parity, 1 stop bit",
        "transmit_policy": "No serial writes; passive receive-only capture",
        "scenario": args.scenario,
    }

    try:
        connection = open_passive_serial(args.port)
    except SerialPortError as error:
        raise SystemExit(str(error)) from error

    started_monotonic = time.monotonic()
    received_bytes = 0
    try:
        with connection, CaptureWriter(args.output_root, args.scenario, metadata) as capture:
            print(f"Capturing receive-only traffic from {args.port}.")
            print(f"Writing to: {capture.directory}")
            print("No bytes will be transmitted. Press Ctrl+C to stop.")

            while args.duration == 0 or time.monotonic() - started_monotonic < args.duration:
                data = connection.read(connection.in_waiting or 1)
                if not data:
                    continue

                capture.record(data, datetime.now().astimezone())
                received_bytes += len(data)
                print(
                    f"{datetime.now().astimezone().isoformat(timespec='milliseconds')} "
                    f"RX {len(data)} byte(s): {data.hex(' ')}"
                )
    except KeyboardInterrupt:
        print("\nCapture stopped by operator.")

    elapsed_seconds = time.monotonic() - started_monotonic
    print(f"Captured {received_bytes} byte(s) in {elapsed_seconds:.1f} second(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
