//! CTA-2045 protocol core.
//!
//! Generic CTA-2045 code with no appliance-specific knowledge and no hardware dependencies,
//! so it builds and runs its tests on a PC. Ported from `python/src/ecoport/cta2045/`.

pub mod checksum;
pub mod framing;

pub use checksum::{compute_checksum, verify_checksum};
pub use framing::{frame, unframe, Frame, FrameError, StreamParser};
