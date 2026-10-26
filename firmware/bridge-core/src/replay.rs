//! A heater source that replays recorded readings, for running without a heater.
//!
//! The input is the `timeline.csv` files in `captures/rinnai/*/`:
//!
//! ```text
//! time,opstate,electricity_w,capacity_wh,take_wh,mark,other
//! 08:27:32.444,1 Running Normal,242,12011,4202,,
//! ```
//!
//! Each data row becomes one reading, played back one per poll, looping forever. The
//! recordings have no cumulative energy, so that is always 0.

use anyhow::{anyhow, ensure, Context};

use crate::heater::HeaterSource;
use crate::record::HeaterReading;

pub struct ReplayHeater {
    rows: Vec<HeaterReading>,
    next: usize,
}

impl ReplayHeater {
    /// Parses one or more timelines, played back in the order given.
    ///
    /// The first line of each is a header. Only the first five fields of a row are used,
    /// because the later ones are free-text notes.
    pub fn from_csvs(csvs: &[&str]) -> anyhow::Result<Self> {
        let mut rows = Vec::new();
        for (file, csv) in csvs.iter().enumerate() {
            for (line_no, line) in csv.lines().enumerate().skip(1) {
                let line = line.trim();
                if line.is_empty() {
                    continue;
                }
                let row = parse_row(line)
                    .with_context(|| format!("Timeline {file}, line {}", line_no + 1))?;
                rows.push(row);
            }
        }
        ensure!(!rows.is_empty(), "No readings to replay");

        Ok(Self { rows, next: 0 })
    }

    /// Number of readings in one pass through the recordings.
    pub fn len(&self) -> usize {
        self.rows.len()
    }

    /// Always false: [`Self::from_csvs`] rejects empty input.
    pub fn is_empty(&self) -> bool {
        self.rows.is_empty()
    }
}

impl HeaterSource for ReplayHeater {
    fn poll(&mut self) -> anyhow::Result<HeaterReading> {
        let row = self.rows[self.next];
        self.next = (self.next + 1) % self.rows.len();
        Ok(row)
    }

    fn is_simulated(&self) -> bool {
        true
    }
}

/// Parses `time,opstate,electricity_w,capacity_wh,take_wh[,...]`.
fn parse_row(line: &str) -> anyhow::Result<HeaterReading> {
    let fields: Vec<&str> = line.splitn(6, ',').take(5).map(str::trim).collect();
    ensure!(fields.len() == 5, "Expected at least 5 fields: {line:?}");

    // "1 Running Normal" -> 1
    let op_state = fields[1]
        .split_whitespace()
        .next()
        .ok_or_else(|| anyhow!("Empty opstate"))?
        .parse()
        .with_context(|| format!("Bad opstate {:?}", fields[1]))?;
    let number = |i: usize| -> anyhow::Result<u32> {
        fields[i]
            .parse()
            .with_context(|| format!("Bad number {:?}", fields[i]))
    };

    Ok(HeaterReading {
        op_state,
        electricity_rate_w: number(2)?,
        electricity_cumulative_wh: 0,
        total_storage_capacity_wh: number(3)?,
        present_energy_take_wh: number(4)?,
    })
}
