#!/usr/bin/env python3
"""Fake EcoPort bridge: replays a Rinnai capture timeline to the Device API, signed like the firmware.

Implements contracts/device-v1 (body shape, headers, HMAC signature). Standard library only.

Examples (from the repository root):
    python tools/fake_device.py --url http://localhost:7071 --device-id WH001 \
        --secret-b64 AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8= --once
    python tools/fake_device.py --url http://localhost:7071 --device-id WH001 --secret-b64 ... --interval 60

Each upload carries --batch samples (15 s apart, ending now), plus an opStateChanged event when the
replayed opstate changes. The first upload also carries a boot event. seq starts at a random base.
Prints "<status> ackSeq=<n>" per upload (or "<status> <body>" on errors). Exit code 1 if the last
upload was not 200 (useful with --once).
"""

from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import hmac
import json
import random
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DEFAULT_CSV = REPO / "captures" / "rinnai" / "2026-10-04T082724-heating-cycle-1" / "timeline.csv"
PATH = "/device/v1/records"
SAMPLE_PERIOD_S = 15
FIRMWARE = "fake-device-0.1"


def sign(secret: bytes, method: str, path: str, timestamp: str, body: bytes) -> str:
    """Same as contracts/device-v1/make_signing_vectors.py."""
    canonical = f"{method}\n{path}\n{timestamp}\n{hashlib.sha256(body).hexdigest()}"
    return base64.b64encode(hmac.new(secret, canonical.encode(), hashlib.sha256).digest()).decode()


def rfc3339_millis(t: datetime) -> str:
    t = t.astimezone(timezone.utc)
    return t.strftime("%Y-%m-%dT%H:%M:%S.") + f"{t.microsecond // 1000:03d}Z"


def rfc3339_seconds(t: datetime) -> str:
    return t.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_rows(csv_path: Path) -> list[dict]:
    """Rows of a capture timeline: opstate is the leading integer of the opstate column."""
    rows = []
    with csv_path.open(newline="", encoding="utf-8") as f:
        reader = csv.reader(f)
        next(reader, None)  # header
        for fields in reader:
            if len(fields) < 5 or not fields[1].strip():
                continue
            try:
                rows.append(
                    {
                        "opState": int(fields[1].strip().split()[0]),
                        "electricityRateW": int(fields[2]),
                        "totalStorageCapacityWh": int(fields[3]),
                        "presentEnergyTakeWh": int(fields[4]),
                    }
                )
            except ValueError:
                continue
    if not rows:
        raise SystemExit(f"no usable rows in {csv_path}")
    return rows


class FakeDevice:
    def __init__(self, args: argparse.Namespace) -> None:
        self.url = args.url.rstrip("/") + PATH
        self.device_id = args.device_id
        self.secret = base64.b64decode(args.secret_b64)
        self.rows = load_rows(Path(args.csv))
        self.batch = args.batch
        self.boot_id = f"{random.getrandbits(32):08x}"
        self.seq = random.randrange(0, 2**31)
        self.cursor = 0
        self.last_opstate: int | None = None
        self.started = time.monotonic()
        self.booted = False

    def uptime_ms(self, at: datetime, now: datetime) -> int:
        elapsed_ms = int((time.monotonic() - self.started) * 1000)
        return max(0, elapsed_ms - int((now - at).total_seconds() * 1000)) + 1000

    def next_seq(self) -> int:
        self.seq += 1
        return self.seq

    def build_records(self, now: datetime) -> list[dict]:
        records = []
        if not self.booted:
            at = now - timedelta(seconds=SAMPLE_PERIOD_S * self.batch)
            records.append(
                {
                    "seq": self.next_seq(),
                    "kind": "event",
                    "uptimeMs": self.uptime_ms(at, now),
                    "t": rfc3339_millis(at),
                    "type": "boot",
                    "data": {"resetReason": "PowerOn"},
                }
            )
            self.booted = True
        for i in range(self.batch):
            at = now - timedelta(seconds=SAMPLE_PERIOD_S * (self.batch - 1 - i))
            row = self.rows[self.cursor]
            self.cursor = (self.cursor + 1) % len(self.rows)
            if self.last_opstate is not None and row["opState"] != self.last_opstate:
                records.append(
                    {
                        "seq": self.next_seq(),
                        "kind": "event",
                        "uptimeMs": self.uptime_ms(at, now),
                        "t": rfc3339_millis(at),
                        "type": "opStateChanged",
                        "data": {"from": self.last_opstate, "to": row["opState"]},
                    }
                )
            self.last_opstate = row["opState"]
            records.append(
                {
                    "seq": self.next_seq(),
                    "kind": "sample",
                    "uptimeMs": self.uptime_ms(at, now),
                    "t": rfc3339_millis(at),
                    "opState": row["opState"],
                    "commodity": {
                        "electricityRateW": row["electricityRateW"],
                        "electricityCumulativeWh": 0,
                        "totalStorageCapacityWh": row["totalStorageCapacityWh"],
                        "presentEnergyTakeWh": row["presentEnergyTakeWh"],
                    },
                    "link": {"ok": True, "retries": 0, "naks": 0},
                }
            )
        return records

    def upload(self) -> int:
        now = datetime.now(timezone.utc)
        payload = {
            "deviceId": self.device_id,
            "bootId": self.boot_id,
            "firmware": FIRMWARE,
            "simulated": True,
            "records": self.build_records(now),
        }
        body = json.dumps(payload, separators=(",", ":")).encode()
        timestamp = rfc3339_seconds(now)
        request = urllib.request.Request(
            self.url,
            data=body,
            method="POST",
            headers={
                "Content-Type": "application/json",
                "X-Device-Id": self.device_id,
                "X-Timestamp": timestamp,
                "X-Signature": sign(self.secret, "POST", PATH, timestamp, body),
            },
        )
        n = len(payload["records"])
        try:
            with urllib.request.urlopen(request, timeout=10) as resp:
                ack = json.loads(resp.read())
                print(f"{resp.status} ackSeq={ack['ackSeq']} ({n} records)", flush=True)
                return resp.status
        except urllib.error.HTTPError as e:
            detail = e.read()[:200].decode(errors="replace")
            print(f"{e.code} {detail}".rstrip(), flush=True)
            return e.code
        except urllib.error.URLError as e:
            print(f"transport error: {e.reason}", flush=True)
            return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--url", required=True, help="Device API base URL, e.g. http://localhost:7071")
    p.add_argument("--device-id", required=True)
    p.add_argument("--secret-b64", required=True, help="Base64 of the 32-byte device secret")
    p.add_argument("--csv", default=str(DEFAULT_CSV), help="capture timeline.csv to replay")
    p.add_argument("--interval", type=float, default=60, help="seconds between uploads")
    p.add_argument("--batch", type=int, default=4, help="samples per upload")
    p.add_argument("--once", action="store_true", help="send one upload and exit")
    args = p.parse_args()

    device = FakeDevice(args)
    while True:
        status = device.upload()
        if args.once:
            return 0 if status == 200 else 1
        time.sleep(args.interval)


if __name__ == "__main__":
    sys.exit(main())
