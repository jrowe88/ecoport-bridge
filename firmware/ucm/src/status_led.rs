//! Status indicators on the board's WS2812 LEDs.
//!
//! LED 0 shows network and upload status with "breathing" patterns. LED 1, on boards that
//! have it ([`LED_COUNT`] is 2), shows the heater link with a steady dim color.
//!
//! The animation runs in its own thread (a FreeRTOS task under the hood), so it keeps
//! animating while other code blocks, for example while connecting to Wi-Fi. Other code
//! changes what is shown through [`StatusLed`], which stores into shared atomics.

use std::f32::consts::PI;
use std::sync::atomic::{AtomicU8, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant};

use esp_idf_svc::hal::delay::FreeRtos;
use esp_idf_svc::hal::gpio::OutputPin;

use crate::board::LED_COUNT;
use crate::ws2812::{Rgb, Ws2812};

/// Peak brightness, `0.0..=1.0`. The LED is very bright at full power.
const MAX_BRIGHTNESS: f32 = 0.75;

/// Bottom of the fade, as a fraction of [`MAX_BRIGHTNESS`].
///
/// Fading all the way to zero looks steppy at the dim end, because the lowest 8-bit
/// levels are large relative jumps (1 -> 2 doubles the brightness).
const MIN_BRIGHTNESS: f32 = 0.05;

/// Time between LED updates.
///
/// FreeRTOS delays round up to whole ticks (10 ms at the default `CONFIG_FREERTOS_HZ=100`),
/// so values below 10 ms still sleep for 10 ms.
const FRAME_INTERVAL: Duration = Duration::from_millis(10);

/// Stack size for the LED thread, set explicitly so it doesn't depend on
/// `CONFIG_PTHREAD_TASK_STACK_SIZE_DEFAULT`. The animation itself needs very little;
/// most of this is headroom for log formatting.
const TASK_STACK_SIZE: usize = 4096;

/// What LED 0 is currently showing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum LedStatus {
    /// Quick blue pulse.
    Connecting = 0,
    /// Slow green breathe.
    Connected = 1,
    /// Fast red pulse.
    Failed = 2,
    /// Steady brightness, cycling through the color wheel. Handy for testing the LED.
    Rainbow = 3,
    /// Amber breathe: Wi-Fi is up but uploads are failing.
    UploadFailing = 4,
}

/// What LED 1 (when present) is currently showing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum HeaterStatus {
    /// Off: the heater hasn't been polled yet.
    Unknown = 0,
    /// Dim purple: readings come from a replayed recording.
    Simulated = 1,
    /// Dim green: the heater answered the last poll.
    Ok = 2,
    /// Red: the last heater poll failed.
    Fault = 3,
}

/// How a status is drawn.
enum Pattern {
    /// Fades `color` in and out once per `period`.
    Breathe { color: Rgb, period: Duration },
    /// Steps through every hue once per `period`, at constant brightness.
    Rainbow { period: Duration },
}

impl Pattern {
    const fn period(&self) -> Duration {
        match self {
            Self::Breathe { period, .. } | Self::Rainbow { period } => *period,
        }
    }

    /// The color to show at `phase` (`0.0..1.0`) through the pattern's cycle.
    fn color_at(&self, phase: f32) -> Rgb {
        match self {
            Self::Breathe { color, .. } => color.scaled(breathe_brightness(phase)),
            Self::Rainbow { .. } => Rgb::from_hue(phase).scaled(MAX_BRIGHTNESS),
        }
    }
}

impl LedStatus {
    const fn from_u8(value: u8) -> Self {
        match value {
            1 => Self::Connected,
            2 => Self::Failed,
            3 => Self::Rainbow,
            4 => Self::UploadFailing,
            _ => Self::Connecting,
        }
    }

    const fn pattern(self) -> Pattern {
        match self {
            Self::Connecting => Pattern::Breathe {
                color: Rgb::new(0, 80, 255),
                period: Duration::from_millis(1000),
            },
            Self::Connected => Pattern::Breathe {
                color: Rgb::new(40, 255, 0),
                period: Duration::from_millis(3000),
            },
            Self::Failed => Pattern::Breathe {
                color: Rgb::new(255, 0, 0),
                period: Duration::from_millis(500),
            },
            Self::Rainbow => Pattern::Rainbow {
                period: Duration::from_millis(6000),
            },
            Self::UploadFailing => Pattern::Breathe {
                color: Rgb::new(255, 110, 0),
                period: Duration::from_millis(2000),
            },
        }
    }
}

impl HeaterStatus {
    const fn from_u8(value: u8) -> Self {
        match value {
            1 => Self::Simulated,
            2 => Self::Ok,
            3 => Self::Fault,
            _ => Self::Unknown,
        }
    }

    /// The steady color for this status. Dim, so it doesn't compete with LED 0.
    const fn color(self) -> Rgb {
        match self {
            Self::Unknown => Rgb::new(0, 0, 0),
            Self::Simulated => Rgb::new(24, 0, 32),
            Self::Ok => Rgb::new(0, 24, 0),
            Self::Fault => Rgb::new(160, 0, 0),
        }
    }
}

/// Handle to the running LED animation thread. Cheap to clone; all clones control the
/// same LEDs.
#[derive(Clone)]
pub struct StatusLed {
    status: Arc<AtomicU8>,
    heater: Arc<AtomicU8>,
}

impl StatusLed {
    /// Spawns the animation thread, driving [`LED_COUNT`] WS2812 LEDs on `pin`.
    ///
    /// The thread owns the LEDs for the rest of the program; the returned handle is
    /// only used to change what they show.
    pub fn start(pin: impl OutputPin + Send + 'static) -> anyhow::Result<Self> {
        let led = Self {
            status: Arc::new(AtomicU8::new(LedStatus::Connecting as u8)),
            heater: Arc::new(AtomicU8::new(HeaterStatus::Unknown as u8)),
        };
        let thread_led = led.clone();

        thread::Builder::new()
            .name("status_led".into())
            .stack_size(TASK_STACK_SIZE)
            .spawn(move || {
                if let Err(e) = animate(pin, &thread_led) {
                    log::error!("Status LED could not start: {e:?}");
                }
            })?;

        Ok(led)
    }

    /// Sets what LED 0 shows.
    pub fn set_status(&self, status: LedStatus) {
        self.status.store(status as u8, Ordering::Relaxed);
    }

    /// Sets what LED 1 shows. Has no visible effect on single-LED boards.
    pub fn set_heater_status(&self, status: HeaterStatus) {
        self.heater.store(status as u8, Ordering::Relaxed);
    }
}

/// Body of the LED thread: renders the current statuses forever.
///
/// Only returns if the LEDs can't be set up. Errors while sending a frame are logged and
/// the animation carries on, so one glitch doesn't freeze the status display.
fn animate(pin: impl OutputPin, led: &StatusLed) -> anyhow::Result<()> {
    let mut strip = Ws2812::new(pin)?;

    // Position within LED 0's current pattern cycle, `0.0..1.0`.
    let mut phase = 0.0_f32;
    let mut last_frame = Instant::now();
    // Logs only the first error of a run of failures, instead of 100 per second.
    let mut send_failing = false;
    let mut colors = [Rgb::default(); LED_COUNT];

    loop {
        let pattern = LedStatus::from_u8(led.status.load(Ordering::Relaxed)).pattern();
        colors[0] = pattern.color_at(phase);
        if let Some(heater) = colors.get_mut(1) {
            *heater = HeaterStatus::from_u8(led.heater.load(Ordering::Relaxed)).color();
        }

        match strip.set_colors(&colors) {
            Ok(()) => send_failing = false,
            Err(e) if !send_failing => {
                log::warn!("Status LED update failed: {e}");
                send_failing = true;
            }
            Err(_) => {}
        }
        FreeRtos::delay_ms(FRAME_INTERVAL.as_millis() as u32);

        // Advance by the real elapsed time rather than FRAME_INTERVAL: the delay rounds up
        // to whole RTOS ticks, and sending takes time too. This keeps the animation speed
        // independent of the frame rate.
        let now = Instant::now();
        let elapsed = (now - last_frame).as_secs_f32();
        phase = (phase + elapsed / pattern.period().as_secs_f32()).fract();
        last_frame = now;
    }
}

/// Brightness for a point in the breathe cycle (`phase` in `0.0..1.0`).
fn breathe_brightness(phase: f32) -> f32 {
    // Raised cosine: a smooth 0 -> 1 -> 0 curve with gentle ease-in and ease-out.
    let level = (1.0 - (2.0 * PI * phase).cos()) / 2.0;
    // Squaring is a cheap gamma correction: the eye is more sensitive to changes at low
    // brightness, so this makes the fade look even.
    let perceived = level * level;

    (MIN_BRIGHTNESS + (1.0 - MIN_BRIGHTNESS) * perceived) * MAX_BRIGHTNESS
}
