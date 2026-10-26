//! Replays the recorded Rinnai traffic in `captures/` through the stream parser.

use std::fs;
use std::path::{Path, PathBuf};

use cta2045::StreamParser;

fn captures_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../captures/rinnai")
}

/// Every received byte in a `capture.jsonl` file, in order.
///
/// Each line is a JSON object with a `"hex"` field of space-separated bytes. A plain string
/// search is enough for this fixed format and avoids a JSON dependency.
fn capture_bytes(path: &Path) -> Vec<u8> {
    const KEY: &str = "\"hex\":\"";
    fs::read_to_string(path)
        .unwrap()
        .lines()
        .filter_map(|line| {
            let start = line.find(KEY)? + KEY.len();
            let len = line[start..].find('"')?;
            Some(line[start..start + len].to_owned())
        })
        .flat_map(|hex| {
            hex.split_whitespace()
                .map(|b| u8::from_str_radix(b, 16).unwrap())
                .collect::<Vec<_>>()
        })
        .collect()
}

#[test]
fn every_capture_parses_cleanly() {
    let mut checked = 0;
    for entry in fs::read_dir(captures_dir()).unwrap() {
        let path = entry.unwrap().path().join("capture.jsonl");
        if !path.exists() {
            continue;
        }
        let bytes = capture_bytes(&path);
        if bytes.is_empty() {
            continue;
        }

        let mut parser = StreamParser::new();
        let frames = parser.feed(&bytes);
        let name = path
            .parent()
            .unwrap()
            .file_name()
            .unwrap()
            .to_string_lossy();

        assert!(!frames.is_empty(), "{name}: no frames");
        // In sessions where the UCM was transmitting (they have a transmit.jsonl),
        // capture.jsonl is missing the header of some heater replies; session.log has the
        // full frames. Only passive captures are expected to be junk-free.
        let passive = !path.with_file_name("transmit.jsonl").exists();
        if passive {
            assert_eq!(parser.dropped_bytes(), 0, "{name}: junk bytes");
        }
        checked += 1;
    }
    assert!(checked > 0, "no captures found in {:?}", captures_dir());
}
