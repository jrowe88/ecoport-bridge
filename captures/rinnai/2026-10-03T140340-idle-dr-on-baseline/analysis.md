# Passive Capture Analysis: DR Enabled While Idle

> **Update (decoded):** with the CTA-2045-B spec, frames A–D are now
> identified as UCM-discovery queries with valid checksums. A/B/C are
> Message Type Supported Queries for Basic DR, Intermediate DR, and
> Data-Link; D is a Data-Link "Query: Maximum Payload Length". See
> [`../2026-10-03T144506-idle-to-dr-on-transition/analysis.md`](../2026-10-03T144506-idle-to-dr-on-transition/analysis.md).
> The original analysis is below, unchanged.

**Capture:** `2026-10-03T140340-idle-dr-on-baseline`

**Serial:** COM4, 19,200 baud, 8 data bits, no parity, 1 stop bit

**Capture policy:** receive-only; the laptop sent no serial bytes
**Duration:** 2026-10-03 14:03:49 to 14:08:39 local time

## Comparison

| Capture | DR state | Raw bytes | Result |
|---|---|---:|---|
| `2026-10-03T134950-idle-baseline` | Off | 0 | No received traffic during the capture. |
| This capture | On | 720 | Repeating, structured frame sequence received. |

This is evidence that enabling DR controls whether the Rinnai emits this
traffic in this operating state. It is not yet proof that it will never
communicate with DR disabled in every appliance state, or that the exact
same behavior applies to all REHP65 firmware revisions.

## Reconstructed frames

The serial reader delivered 256 USB/serial chunks, which are not message
boundaries. Reconstructing directly from `capture.bin` yields 111 complete
frames:

| Frame | Count | Bytes | Interpretation |
|---|---:|---|---|
| A | 30 | `08 01 00 00 7E CD` | Repeating frame; prefix is consistent with the documented Basic DR type (`08 01`), but semantics remain unconfirmed. |
| B | 27 | `08 02 00 00 7A D0` | Repeating frame; prefix is consistent with the documented Intermediate DR type (`08 02`), but semantics remain unconfirmed. |
| C | 27 | `08 03 00 00 76 D3` | Repeating frame; `08 03` is not yet identified. |
| D | 27 | `08 03 00 02 18 00 BA 75` | Repeating frame; `08 03` is not yet identified. |

The final A triplet is complete, but the capture ended before the rest of
that cycle. That accounts for A occurring three more times than B/C/D.

## Cadence

Each complete cycle contains three consecutive copies of A, then B, then C,
then D:

```text
A A A  B B B  C C C  D D D  [about 20.4 seconds silent]  repeat
```

- Frames within the active burst start about one second apart (roughly
  0.99–1.22 seconds in this capture).
- The final D of one burst to the next A is about 20.4 seconds.
- Start-to-start cycle period is therefore about **32.4 seconds**.
- Nine full A/B/C/D cycles were captured, followed by the first A triplet of
  a tenth cycle.

## What we can conclude now

1. The wiring and serial settings are producing structured, repeatable data;
   this is not random electrical noise.
2. The passive sniffer is correctly preserving raw data even when USB serial
   reads split a single frame across multiple events.
3. DR enabled is strongly correlated with this repeating traffic while the
   heater is idle.
4. The leading bytes `08 01` and `08 02` align with publicly documented
   CTA-2045 Basic DR and Intermediate DR identifiers, respectively. This is
   a useful lead, **not a decode**; the remaining fields and trailing bytes
   have not been validated as payload, length, sequence, or checksum fields.

## Recommended next captures

1. Repeat the **DR-off idle** capture for at least 10 minutes with the same
   physical wiring and serial settings to confirm the absence of traffic.
2. Start a capture **before** pressing the DR control-panel button, leave it
   running while turning DR on, and continue for at least two complete
   cycles. This will timestamp the transition rather than comparing separate
   sessions.
3. With DR left on, make one normal control-panel change at a time (mode,
   then a 1°F setpoint change), following
   [`docs/rinnai-control-panel-capture-plan.md`](../../../docs/rinnai-control-panel-capture-plan.md).
   This can reveal whether any payload fields or frame cadence respond.
4. Do not transmit a CTA-2045 request yet. First identify framing and
   checksum behavior from repeated passive captures and the EPRI simulator.
