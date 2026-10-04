import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))

from ucm_timeline import changes, parse

COMMODITY = (
    "{t} RX 08 02 00 2a 06 80 ... GetCommodityRead reply: "
    "electricity consumed (W, Wh)=rate={w} cumulative=0 (estimated), "
    "total energy storage capacity (Wh)=rate=0 cumulative={cap} (estimated), "
    "present energy storage capacity (Wh)=rate=0 cumulative={take} (estimated)"
)

LOG = [
    "08:00:00.000 UCM on COM4; writing to x. Ctrl+C to stop.",
    "08:00:01.000 RX 08 01 00 02 13 00 d5 61      operational state 0 (Idle Normal)",
    COMMODITY.format(t="08:00:02.000", w=0, cap=12011, take=631),
    COMMODITY.format(t="08:00:17.000", w=0, cap=12011, take=649),
    "08:00:20.000 MARK setpoint 120 -> 125",
    "08:00:31.000 RX 08 01 00 02 13 01 d3 62      operational state 1 (Running Normal)",
    COMMODITY.format(t="08:00:32.000", w=242, cap=12500, take=1200),
    COMMODITY.format(t="09:00:32.000", w=242, cap=12500, take=0),
]


def test_parse_collects_state_commodity_and_marks():
    rows = parse(LOG)
    assert [r.take_wh for r in rows] == ["631", "649", "649", "1200", "0"]
    assert rows[2].mark == "setpoint 120 -> 125" and rows[2].opstate == "0 Idle Normal"
    assert rows[3].opstate == "1 Running Normal" and rows[3].electricity_w == "242"
    assert rows[3].capacity_wh == "12500"


def test_changes_skip_jitter_and_report_slope():
    shown = changes(parse(LOG))
    times = [r.time for r, _ in shown]
    assert "08:00:17.000" not in times  # 18 Wh jitter with no other change
    assert times == ["08:00:02.000", "08:00:20.000", "08:00:32.000", "09:00:32.000"]
    assert round(shown[-1][1]) == -1200
