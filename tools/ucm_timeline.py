#!/usr/bin/env python3
"""Turn a ucm.py session.log into a timeline of heater state.

Collects operational state, Commodity Read values and operator MARK lines,
writes them all to timeline.csv in the capture directory, and prints only the
rows where something changed (plus the energy-take slope since the last change).
Marks of the form ``sp 118`` (typed right after changing the setpoint) also
produce calibration.csv: setpoint -> total energy storage capacity.

Usage:
    python tools/ucm_timeline.py captures/rinnai/<capture-dir>
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

TIME = re.compile(r"^(\d\d:\d\d:\d\d\.\d{3}) ")
OPSTATE = re.compile(r"RX .*operational state (\d+) \(([^)]*)\)")
COMMODITY = re.compile(r"([a-z][a-z ]*?) \([^)]*\)=rate=(\S+) cumulative=(\S+)")
MARK = re.compile(r" MARK (.*)$")
SETPOINT_MARK = re.compile(r"^sp\s*(\d+(?:\.\d+)?)\b", re.IGNORECASE)
SETTLE_SECONDS = 16  # one 15 s poll after the panel change

TAKE = "present energy storage capacity"
CAPACITY = "total energy storage capacity"
ELECTRICITY = "electricity consumed"
TAKE_JUMP_WH = 100  # a jump larger than a few 18 Wh steps between samples is worth showing


@dataclass
class Row:
    time: str
    opstate: str = ""
    electricity_w: str = ""
    capacity_wh: str = ""
    take_wh: str = ""
    mark: str = ""
    other: dict[str, str] = field(default_factory=dict)


def parse(lines: list[str]) -> list[Row]:
    rows: list[Row] = []
    state = Row("")
    for line in lines:
        stamp = TIME.match(line)
        if not stamp:
            continue
        if mark := MARK.search(line):
            rows.append(Row(stamp[1], state.opstate, state.electricity_w, state.capacity_wh,
                            state.take_wh, mark=mark[1]))
            continue
        if op := OPSTATE.search(line):
            state.opstate = f"{op[1]} {op[2]}"
            continue
        if "GetCommodityRead reply" in line:
            other = {}
            for name, rate, cumulative in COMMODITY.findall(line.split("reply:", 1)[1]):
                name = name.strip()
                if name == ELECTRICITY:
                    state.electricity_w = rate
                elif name == CAPACITY:
                    state.capacity_wh = cumulative
                elif name == TAKE:
                    state.take_wh = cumulative
                else:
                    other[name] = f"{rate}/{cumulative}"
            rows.append(Row(stamp[1], state.opstate, state.electricity_w, state.capacity_wh,
                            state.take_wh, other=other))
    return rows


def _seconds(clock: str) -> float:
    hours, minutes, seconds = clock.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def changes(rows: list[Row]) -> list[tuple[Row, float | None]]:
    """Rows where state changed, each with the take slope (Wh/h) since the previous shown row."""
    shown: list[tuple[Row, float | None]] = []
    previous: Row | None = None
    for i, row in enumerate(rows):
        last = i == len(rows) - 1
        jump = (
            previous is not None and row.take_wh and previous.take_wh
            and abs(int(row.take_wh) - int(previous.take_wh)) > TAKE_JUMP_WH
        )
        key = (row.opstate, row.electricity_w, row.capacity_wh, tuple(row.other.items()))
        prev_key = None if previous is None else (
            previous.opstate, previous.electricity_w, previous.capacity_wh, tuple(previous.other.items())
        )
        if previous is None or row.mark or key != prev_key or jump or last:
            slope = None
            if shown and row.take_wh and shown[-1][0].take_wh:
                hours = (_seconds(row.time) - _seconds(shown[-1][0].time)) / 3600
                if hours > 0:
                    slope = (int(row.take_wh) - int(shown[-1][0].take_wh)) / hours
            shown.append((row, slope))
        if not row.mark:
            previous = row
    return shown


def calibration(rows: list[Row]) -> list[tuple[float, str, str]]:
    """(setpoint, time, capacity_wh) for each ``sp <temp>`` mark, read one poll after the mark."""
    table = []
    for i, row in enumerate(rows):
        match = SETPOINT_MARK.match(row.mark) if row.mark else None
        if not match:
            continue
        due = _seconds(row.time) + SETTLE_SECONDS
        sample = next(
            (r for r in rows[i + 1:] if not r.mark and r.capacity_wh and _seconds(r.time) >= due), None
        )
        if sample:
            table.append((float(match[1]), sample.time, sample.capacity_wh))
    return table


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("capture", type=Path, help="Capture directory containing session.log")
    args = parser.parse_args(argv)
    rows = parse((args.capture / "session.log").read_text(encoding="utf-8").splitlines())
    if not rows:
        print("No opstate/commodity/MARK lines found.")
        return 1
    with (args.capture / "timeline.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["time", "opstate", "electricity_w", "capacity_wh", "take_wh", "mark", "other"])
        for r in rows:
            other = "; ".join(f"{k}={v}" for k, v in r.other.items())
            writer.writerow([r.time, r.opstate, r.electricity_w, r.capacity_wh, r.take_wh, r.mark, other])
    print(f"{'time':<12} {'opstate':<20} {'elec W':>7} {'cap Wh':>7} {'take Wh':>8} {'Wh/h':>7}  mark")
    for r, slope in changes(rows):
        rate = f"{slope:+.0f}" if slope is not None else ""
        print(f"{r.time:<12} {r.opstate:<20} {r.electricity_w:>7} {r.capacity_wh:>7} {r.take_wh:>8} {rate:>7}  {r.mark}")
    print(f"\n{len(rows)} rows -> {args.capture / 'timeline.csv'}")
    table = calibration(rows)
    if table:
        with (args.capture / "calibration.csv").open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["setpoint", "time", "capacity_wh"])
            writer.writerows(table)
        print(f"\n{'setpoint':>8} {'capacity Wh':>12} {'Wh/deg vs prev':>15}")
        previous = None
        for setpoint, _time, capacity in table:
            per_degree = ""
            if previous and setpoint != previous[0]:
                per_degree = f"{(int(capacity) - int(previous[1])) / (setpoint - previous[0]):.0f}"
            print(f"{setpoint:>8g} {capacity:>12} {per_degree:>15}")
            previous = (setpoint, capacity)
        print(f"-> {args.capture / 'calibration.csv'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
