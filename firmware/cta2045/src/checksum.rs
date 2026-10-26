//! Checksum for CTA-2045 link-layer frames.
//!
//! CTA-2045 frames end with a two-byte Fletcher-style checksum seeded with `0xAA`. This
//! reproduces the trailing two bytes of every frame captured from the Rinnai REHP65
//! (see `docs/protocol.md`, Confirmed).

/// Returns the two checksum bytes (MSB, LSB) for `data`.
///
/// `data` is the frame header and payload, excluding the checksum itself.
pub fn compute_checksum(data: &[u8]) -> [u8; 2] {
    let mut c1: u32 = 0xAA;
    let mut c2: u32 = 0;
    for &byte in data {
        c1 = (c1 + u32::from(byte)) % 255;
        c2 = (c2 + c1) % 255;
    }
    let msb = 255 - ((c1 + c2) % 255);
    let lsb = 255 - ((c1 + msb) % 255);
    [msb as u8, lsb as u8]
}

/// Returns true if the last two bytes of `frame` are a valid checksum for the rest.
pub fn verify_checksum(frame: &[u8]) -> bool {
    match frame.len().checked_sub(2) {
        Some(split) => compute_checksum(&frame[..split]) == frame[split..],
        None => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::framing::tests::hex;

    #[test]
    fn matches_observed_and_spec_example_frames() {
        for text in [
            "08 01 00 00 7e cd",
            "08 02 00 00 7a d0",
            "08 03 00 00 76 d3",
            "08 03 00 02 18 00 ba 75",
            "08 04 00 00 72 d6",
            "08 03 00 02 16 00 c0 71",
        ] {
            let raw = hex(text);
            let split = raw.len() - 2;
            assert_eq!(compute_checksum(&raw[..split]), raw[split..], "{text}");
            assert!(verify_checksum(&raw), "{text}");
        }
    }

    #[test]
    fn rejects_corrupt_and_short_input() {
        assert!(!verify_checksum(&hex("08 01 00 00 7e ce")));
        assert!(!verify_checksum(&[0x08]));
        assert!(!verify_checksum(&[]));
    }
}
