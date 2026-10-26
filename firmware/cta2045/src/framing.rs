//! CTA-2045 link-layer framing.
//!
//! Two packet shapes exist on the wire (CTA-2045-B §6.1, §8.1):
//!
//! - Link-layer ACK/NAK: exactly 2 bytes, `06 00` or `15 <code>`.
//! - Message frame: `msg_type (2) + payload_length (2, big-endian) + payload +
//!   Fletcher checksum (2)`.
//!
//! Both are confirmed against Rinnai REHP65 captures and the spec examples.

use std::fmt;

use crate::checksum::{compute_checksum, verify_checksum};

pub const HEADER_LEN: usize = 4;
pub const CHECKSUM_LEN: usize = 2;
pub const LINK_ACK: [u8; 2] = [0x06, 0x00];
pub const LINK_NAK_PREFIX: u8 = 0x15;
pub const MAX_NAK_CODE: u8 = 0x07;
const LENGTH_MASK: u16 = 0x1FFF;

/// Largest payload length an incomplete frame may claim before it is treated as junk.
const MAX_PLAUSIBLE_PAYLOAD: usize = 4096;
/// [`StreamParser`] never holds more than one maximum-size frame of unparsed bytes.
const MAX_PENDING: usize = HEADER_LEN + MAX_PLAUSIBLE_PAYLOAD + CHECKSUM_LEN;

/// A valid packet (message frame or link ACK/NAK) found in a byte stream.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Frame {
    /// Position of the first byte in the stream.
    pub offset: usize,
    /// The whole packet, including header and checksum.
    pub raw: Vec<u8>,
}

impl Frame {
    pub fn is_link_ack(&self) -> bool {
        self.raw == LINK_ACK
    }

    pub fn is_link_nak(&self) -> bool {
        self.raw.len() == 2 && self.raw[0] == LINK_NAK_PREFIX
    }

    pub fn is_link_control(&self) -> bool {
        self.is_link_ack() || self.is_link_nak()
    }

    /// The two message-type bytes, or `None` for a link ACK/NAK.
    pub fn msg_type(&self) -> Option<[u8; 2]> {
        (!self.is_link_control()).then(|| [self.raw[0], self.raw[1]])
    }

    /// The payload between header and checksum (empty for a link ACK/NAK).
    pub fn payload(&self) -> &[u8] {
        if self.is_link_control() {
            &[]
        } else {
            &self.raw[HEADER_LEN..self.raw.len() - CHECKSUM_LEN]
        }
    }
}

/// Why [`unframe`] rejected a frame.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameError {
    TooShort,
    LengthMismatch,
    BadChecksum,
}

impl fmt::Display for FrameError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::TooShort => "frame too short",
            Self::LengthMismatch => "frame length does not match header",
            Self::BadChecksum => "bad checksum",
        })
    }
}

impl std::error::Error for FrameError {}

/// Builds a complete frame (header, payload, checksum).
///
/// # Panics
///
/// If `payload` is longer than the 13-bit length field allows (8,191 bytes).
pub fn frame(msg_type: [u8; 2], payload: &[u8]) -> Vec<u8> {
    assert!(
        payload.len() <= usize::from(LENGTH_MASK),
        "CTA-2045 payload too long"
    );
    let mut raw = Vec::with_capacity(HEADER_LEN + payload.len() + CHECKSUM_LEN);
    raw.extend_from_slice(&msg_type);
    raw.extend_from_slice(&(payload.len() as u16).to_be_bytes());
    raw.extend_from_slice(payload);
    let checksum = compute_checksum(&raw);
    raw.extend_from_slice(&checksum);
    raw
}

/// Validates a single complete frame and returns its payload.
pub fn unframe(data: &[u8]) -> Result<&[u8], FrameError> {
    if data.len() < HEADER_LEN + CHECKSUM_LEN {
        return Err(FrameError::TooShort);
    }
    if data.len() != HEADER_LEN + payload_length(data) + CHECKSUM_LEN {
        return Err(FrameError::LengthMismatch);
    }
    if !verify_checksum(data) {
        return Err(FrameError::BadChecksum);
    }
    Ok(&data[HEADER_LEN..data.len() - CHECKSUM_LEN])
}

/// The payload length from a frame header. `header` must be at least [`HEADER_LEN`] bytes.
fn payload_length(header: &[u8]) -> usize {
    usize::from(u16::from_be_bytes([header[2], header[3]]) & LENGTH_MASK)
}

/// What can be parsed at the start of a buffer.
enum Match {
    /// A valid packet of this many bytes.
    Complete(usize),
    /// Could be a valid packet once more bytes arrive.
    Incomplete,
    /// No valid packet starts here.
    Invalid,
}

fn match_packet(data: &[u8]) -> Match {
    let first = data[0];
    if first == LINK_ACK[0] || first == LINK_NAK_PREFIX {
        let Some(&second) = data.get(1) else {
            return Match::Incomplete;
        };
        let valid = (first == LINK_ACK[0] && second == LINK_ACK[1])
            || (first == LINK_NAK_PREFIX && (0x01..=MAX_NAK_CODE).contains(&second));
        return if valid {
            Match::Complete(2)
        } else {
            Match::Invalid
        };
    }
    if data.len() < HEADER_LEN {
        return Match::Incomplete;
    }
    let end = HEADER_LEN + payload_length(data) + CHECKSUM_LEN;
    if end > data.len() {
        Match::Incomplete
    } else if verify_checksum(&data[..end]) {
        Match::Complete(end)
    } else {
        Match::Invalid
    }
}

/// True if an incomplete packet at the start of `buffer` could be real.
///
/// Standard message types begin with 0x08 or 0x09 (§6.1.1); ACK/NAK begin with 0x06/0x15.
fn plausible_start(buffer: &[u8]) -> bool {
    match buffer[0] {
        0x06 | LINK_NAK_PREFIX => true,
        0x08 | 0x09 => buffer.len() < HEADER_LEN || payload_length(buffer) <= MAX_PLAUSIBLE_PAYLOAD,
        _ => false,
    }
}

/// Incrementally parses packets from a live byte stream, resynchronizing past junk.
#[derive(Debug, Default)]
pub struct StreamParser {
    buffer: Vec<u8>,
    offset: usize,
    dropped: usize,
}

impl StreamParser {
    pub fn new() -> Self {
        Self::default()
    }

    /// Bytes received that are not yet part of a complete packet.
    pub fn pending(&self) -> &[u8] {
        &self.buffer
    }

    /// Total bytes discarded as junk so far.
    pub fn dropped_bytes(&self) -> usize {
        self.dropped
    }

    /// Adds received bytes and returns every packet they complete.
    pub fn feed(&mut self, data: &[u8]) -> Vec<Frame> {
        self.buffer.extend_from_slice(data);
        let mut frames = Vec::new();
        while !self.buffer.is_empty() {
            match match_packet(&self.buffer) {
                Match::Complete(end) => {
                    let raw = self.buffer.drain(..end).collect();
                    frames.push(Frame {
                        offset: self.offset,
                        raw,
                    });
                    self.offset += end;
                }
                Match::Invalid => self.discard(1),
                Match::Incomplete => {
                    if plausible_start(&self.buffer) {
                        break;
                    }
                    // The start can't become a real packet; skip to the next complete one.
                    let next = (1..self.buffer.len())
                        .find(|&j| matches!(match_packet(&self.buffer[j..]), Match::Complete(_)));
                    match next {
                        Some(skip) => self.discard(skip),
                        None => break,
                    }
                }
            }
        }
        if self.buffer.len() > MAX_PENDING {
            self.discard(self.buffer.len() - MAX_PENDING);
        }
        frames
    }

    /// Discards a partial packet. Call after an inter-byte timeout.
    ///
    /// Returns the number of bytes discarded.
    pub fn flush_stale(&mut self) -> usize {
        let count = self.buffer.len();
        self.discard(count);
        count
    }

    fn discard(&mut self, count: usize) {
        self.buffer.drain(..count);
        self.offset += count;
        self.dropped += count;
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    /// Parses space-separated hex, e.g. `"08 01 00 00"`.
    pub(crate) fn hex(text: &str) -> Vec<u8> {
        text.split_whitespace()
            .map(|b| u8::from_str_radix(b, 16).unwrap())
            .collect()
    }

    const OBSERVED: [&str; 4] = [
        "08 01 00 00 7e cd",
        "08 02 00 00 7a d0",
        "08 03 00 00 76 d3",
        "08 03 00 02 18 00 ba 75",
    ];

    #[test]
    fn frame_round_trip() {
        let raw = frame([0x08, 0x03], &[0x18, 0x00]);
        assert_eq!(raw, hex(OBSERVED[3]));
        assert_eq!(unframe(&raw), Ok(&[0x18, 0x00][..]));
    }

    #[test]
    fn unframe_rejects_bad_frames() {
        assert_eq!(unframe(&hex("08 01 00")), Err(FrameError::TooShort));
        assert_eq!(
            unframe(&hex("08 01 00 01 7e cd")),
            Err(FrameError::LengthMismatch)
        );
        assert_eq!(
            unframe(&hex("08 01 00 00 7e ce")),
            Err(FrameError::BadChecksum)
        );
    }

    fn observed_stream() -> Vec<u8> {
        let mut stream = vec![0xff];
        for text in OBSERVED {
            stream.extend(hex(text));
        }
        stream.push(0x08);
        stream
    }

    #[test]
    fn stream_parser_resyncs_past_junk() {
        let mut parser = StreamParser::new();
        let frames = parser.feed(&observed_stream());

        let raws: Vec<_> = frames.iter().map(|f| f.raw.clone()).collect();
        let expected: Vec<_> = OBSERVED.iter().map(|t| hex(t)).collect();
        assert_eq!(raws, expected);
        assert_eq!(frames[0].offset, 1);
        assert_eq!(parser.dropped_bytes(), 1);
        assert_eq!(parser.pending(), [0x08]);

        assert_eq!(parser.flush_stale(), 1);
        assert!(parser.pending().is_empty());
    }

    #[test]
    fn stream_parser_handles_one_byte_at_a_time() {
        let mut parser = StreamParser::new();
        let frames: Vec<_> = observed_stream()
            .iter()
            .flat_map(|&b| parser.feed(&[b]))
            .collect();
        assert_eq!(frames.len(), OBSERVED.len());
        assert_eq!(parser.dropped_bytes(), 1);
    }

    #[test]
    fn link_control_packets() {
        let mut parser = StreamParser::new();
        let frames = parser.feed(&hex("06 00 15 03"));
        assert!(frames[0].is_link_ack());
        assert!(frames[1].is_link_nak());
        assert_eq!(frames[1].msg_type(), None);
        assert!(frames[1].payload().is_empty());
    }

    #[test]
    fn pending_bytes_stay_bounded() {
        let mut parser = StreamParser::new();
        // A plausible header claiming a 4,096-byte payload, then a long run of filler.
        parser.feed(&hex("08 01 10 00"));
        parser.feed(&vec![0u8; 3 * MAX_PENDING]);
        assert!(parser.pending().len() <= MAX_PENDING);
    }
}
