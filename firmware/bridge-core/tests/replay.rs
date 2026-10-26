//! Replays the recorded Rinnai timelines in `captures/`.

use bridge_core::heater::HeaterSource;
use bridge_core::record::HeaterReading;
use bridge_core::replay::ReplayHeater;

const HEATING_CYCLE: &str =
    include_str!("../../../captures/rinnai/2026-10-04T082724-heating-cycle-1/timeline.csv");
const PANEL_MODES: &str =
    include_str!("../../../captures/rinnai/2026-10-04T123929-panel-modes-1/timeline.csv");

#[test]
fn first_poll_is_first_row() {
    let mut heater = ReplayHeater::from_csvs(&[HEATING_CYCLE]).unwrap();
    assert!(heater.is_simulated());
    assert_eq!(
        heater.poll().unwrap(),
        HeaterReading {
            op_state: 1,
            electricity_rate_w: 242,
            electricity_cumulative_wh: 0,
            total_storage_capacity_wh: 12011,
            present_energy_take_wh: 4202,
        }
    );
}

#[test]
fn wraps_after_the_last_row() {
    let mut heater = ReplayHeater::from_csvs(&[HEATING_CYCLE]).unwrap();
    // 120 data rows after the header.
    assert_eq!(heater.len(), 120);

    let first = heater.poll().unwrap();
    for _ in 1..120 {
        heater.poll().unwrap();
    }
    assert_eq!(heater.poll().unwrap(), first);
}

#[test]
fn panel_modes_include_vacation() {
    let mut heater = ReplayHeater::from_csvs(&[PANEL_MODES]).unwrap();
    let rows: Vec<_> = (0..heater.len()).map(|_| heater.poll().unwrap()).collect();
    assert!(rows.iter().any(|r| r.total_storage_capacity_wh == 0));
}

#[test]
fn several_files_play_back_to_back() {
    let one = ReplayHeater::from_csvs(&[HEATING_CYCLE]).unwrap().len();
    let two = ReplayHeater::from_csvs(&[PANEL_MODES]).unwrap().len();
    let mut both = ReplayHeater::from_csvs(&[HEATING_CYCLE, PANEL_MODES]).unwrap();
    assert_eq!(both.len(), one + two);

    for _ in 0..one {
        both.poll().unwrap();
    }
    // First row of panel-modes-1.
    assert_eq!(both.poll().unwrap().present_energy_take_wh, 649);
}

#[test]
fn rejects_bad_input() {
    assert!(ReplayHeater::from_csvs(&[]).is_err());
    assert!(
        ReplayHeater::from_csvs(&["time,opstate,electricity_w,capacity_wh,take_wh\n"]).is_err()
    );
    assert!(ReplayHeater::from_csvs(&["header\n08:00:00,x Idle,0,1,2\n"]).is_err());
    assert!(ReplayHeater::from_csvs(&["header\n08:00:00,1 Running\n"]).is_err());
}
