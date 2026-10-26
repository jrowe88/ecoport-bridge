//! RFC 3339 timestamp formatting for the device API.
//!
//! Only formatting is needed on the device, so this is a few lines of calendar arithmetic
//! rather than a date/time crate.

const MS_PER_DAY: i64 = 86_400_000;

/// Formats Unix time in milliseconds as `2026-10-09T20:30:00.120Z` (record `t`).
pub fn format_rfc3339_millis(unix_ms: i64) -> String {
    let (date, ms_of_day) = split(unix_ms);
    format!("{date}T{}.{:03}Z", hms(ms_of_day), ms_of_day % 1000)
}

/// Formats Unix time in milliseconds as `2026-10-09T20:31:02Z`, dropping the milliseconds
/// (the `X-Timestamp` header).
pub fn format_rfc3339_seconds(unix_ms: i64) -> String {
    let (date, ms_of_day) = split(unix_ms);
    format!("{date}T{}Z", hms(ms_of_day))
}

/// Splits Unix milliseconds into the `YYYY-MM-DD` date and the milliseconds into that day.
fn split(unix_ms: i64) -> (String, i64) {
    let days = unix_ms.div_euclid(MS_PER_DAY);
    let (y, m, d) = civil_from_days(days);
    (
        format!("{y:04}-{m:02}-{d:02}"),
        unix_ms.rem_euclid(MS_PER_DAY),
    )
}

/// Formats milliseconds into a day as `HH:MM:SS`.
fn hms(ms_of_day: i64) -> String {
    let secs = ms_of_day / 1000;
    format!("{:02}:{:02}:{:02}", secs / 3600, secs / 60 % 60, secs % 60)
}

/// Converts days since 1970-01-01 to a proleptic Gregorian (year, month, day).
///
/// Howard Hinnant's `civil_from_days`: it counts in 400-year eras starting on 1 March, so
/// the leap day falls at the end of each counted year and needs no special case.
fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468; // Shift the epoch to 0000-03-01.
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097); // Day of era, 0..=146_096
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365; // Year of era, 0..=399
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100); // Day of year from 1 March, 0..=365
    let mp = (5 * doy + 2) / 153; // Month from March, 0..=11
    let day = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let month = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    let year = yoe + era * 400 + i64::from(month <= 2);
    (year, month, day)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_millis() {
        assert_eq!(
            format_rfc3339_millis(1_760_041_800_120),
            "2025-10-09T20:30:00.120Z"
        );
    }

    #[test]
    fn formats_epoch() {
        assert_eq!(format_rfc3339_millis(0), "1970-01-01T00:00:00.000Z");
    }

    #[test]
    fn formats_leap_day() {
        assert_eq!(
            format_rfc3339_millis(1_709_164_800_000),
            "2024-02-29T00:00:00.000Z"
        );
    }

    #[test]
    fn seconds_truncates_millis() {
        assert_eq!(
            format_rfc3339_seconds(1_760_041_862_999),
            "2025-10-09T20:31:02Z"
        );
    }
}
