# Passive Capture Analysis: DR Off → DR On Transition

**Capture:** `2026-10-03T144506-idle-to-dr-on-transition`

**Serial:** COM4, 19,200 baud, 8N1 · **Policy:** receive-only (no bytes sent)

**Duration:** 14:45:06 → ~14:55:03 local (~10 minutes)

Reproduce with: `python tools/decode.py captures/rinnai/2026-10-03T144506-idle-to-dr-on-transition`

## Summary

| Metric | Value |
|---|---|
| Raw bytes | 1,326 |
| Valid frames (checksum OK) | 204 |
| Junk / unparsed bytes | 0 |
| First byte after capture start | +72.2 s (≈ when DR was pressed) |
| Last byte after capture start | +597.0 s (traffic still running at end) |
| Complete A/B/C/D cycles | 17 (51 copies of each frame) |

- **Before DR on (0 – ~72 s):** silence, matching the earlier DR-off capture.
- **After DR on:** traffic begins within seconds and is byte-for-byte the
  same 4-frame cycle seen in `2026-10-03T140340-idle-dr-on-baseline`.
  No distinct "first" message — it starts straight into the cycle.
- **Cadence:** identical to the earlier capture — ~1.0–1.2 s between frames,
  ~20.4 s silence, **~32.1 s** start-to-start.
- **Stop:** the capture ended at ~10 min while traffic was still running.
  Operator observed (adapter RX LED) that traffic **stopped about 15 minutes**
  after capture start. That stop is not in this recording.

## Decoded (using ANSI/CTA-2045-B)

Every byte parses as `msg type (2) + payload length (2, big-endian) +
payload + Fletcher checksum (2)` (CTA-2045-B §6.1, Appendix C). All 204
checksums verify.

| Frame | Bytes | Meaning (CTA-2045-B) |
|---|---|---|
| A | `08 01 00 00 7E CD` | Message Type Supported Query — Basic DR (§8.2) |
| B | `08 02 00 00 7A D0` | Message Type Supported Query — Intermediate DR (§8.2) |
| C | `08 03 00 00 76 D3` | Message Type Supported Query — Data-Link (§8.2) |
| D | `08 03 00 02 18 00 BA 75` | Data-Link: Query Maximum Payload Length (opcode `0x18`, §9) |

A zero-length payload of a given message type is how CTA-2045 asks "do you
support this message type?". The receiver must answer each frame with a
2-byte link-layer ACK (`06 00`) or NAK (`15 xx`) within 40–200 ms (§6.1.5.1,
§8.1).

**Interpretation:** the water heater (SGD) is trying to discover a
communications module (UCM). Nobody answers, so it retries each query three
times (§6.1.5.2 recommends three retries with a randomized 100–2000 ms delay),
moves to the next query, then waits ~20 s and starts again. All traffic is
SGD → UCM; there are no `06 00` / `15 xx` frames because no UCM is attached.

## Why it may stop at ~15 minutes (hypothesis)

CTA-2045-B §9.1.3: if no valid communication occurs for more than **15
minutes**, both sides return to defaults. The operator-observed stop at
~15 minutes fits the idea that the Rinnai gives up (or falls back) after 15
minutes without a UCM. To confirm, capture continuously for ≥ 25 minutes
after pressing DR and look for the last frame time and any later resume.

## Next steps

1. **Long capture (passive):** DR on, ≥ 25 min — measure the exact stop time
   and whether discovery resumes later.
2. **DR toggle (passive):** after it stops, turn DR off/on again and confirm
   discovery restarts.
3. **First transmit (later, deliberate):** the smallest standards-conformant
   reply is a link-layer ACK `06 00` to the Basic DR query. This is the first
   step toward acting as a UCM. Do not do this until the hardware safety
   checklist is complete and we explicitly decide to leave receive-only mode.
