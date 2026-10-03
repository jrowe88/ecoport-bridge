# First Active UCM Session

**Capture:** `2026-10-03T161440-first-ucm-probe` · COM4, 19200 8N1
**Tool:** `tools/ucm.py --transmit --probe` (commit `03f342e`) · ~2 minutes
**Heater state:** DR icon on; discovery had been silent for >30 min beforehand.

Files: `session.log` (readable), `capture.bin`/`capture.jsonl` (RX only),
`transmit.jsonl` (our TX). Replay: `python tools/decode.py captures/rinnai/2026-10-03T161440-first-ucm-probe`

## Result: the Rinnai talks to us

| Step | Result |
|---|---|
| Our Supported Queries (Basic, Intermediate, Data-Link) | All link-ACKed (`06 00`) |
| Outside Comm Status = Good (`0E 01`) | Link ACK, then **App ACK** `08 01 00 02 03 0E` (~415 ms later) |
| Query Operational State (`12 00`) | Reply `13 01` = **Running Normal** (both times) |
| GetInformation (`08 02 .. 01 01`) | 16-byte reply, decoded below |
| Heater's own discovery | Resumed immediately; we ACKed all of it |

Link ACK latency from the heater is ~10–20 ms; its application replies come
~400–420 ms after its ACK.

### GetInformation reply

`08 02 00 10 | 01 81 00 41 00 0C 22 00 03 00 04 00 00 00 00 00 | 14 2E`

| Field | Bytes | Value |
|---|---|---|
| Opcodes | `01 81` | GetInformation reply |
| Response code | `00` | Success |
| CTA-2045 version | `41 00` | **"A"** (original CTA-2045, not B) |
| Vendor ID | `0C 22` | 0x0C22 (presumably Rinnai) |
| Device type | `00 03` | **Water Heater – Heat Pump** |
| Device revision | `00 04` | 4 |
| Capability bitmap | `00 00 00 00` | none advertised |
| Reserved | `00` | |

Optional model/serial/firmware fields are not included.

## Problem: max payload negotiation

The heater repeats discovery about every 9–10 s, ending with "Query: Maximum
Payload Length" (`18 00`). We ACK it and answer `08 03 00 02 19 06` (128 bytes):

| Our response sent after our ACK | Heater reaction |
|---|---|
| ~0.9 s (16:14:42, the one time) | Link ACK ✅ |
| ~0.15 s (all 9 later cycles) | No response |
| Retry 0.4–2.0 s later | Link NAK `15 07` (Request Not Supported) |

The successful case and one failed retry were sent at almost the same delay
(0.9 s vs 0.85 s), so delay alone doesn't explain it. **Hypothesis:** the
heater isn't listening right after our ACK and catches only part of an
early reply. After that it treats the retry as unsolicited and NAKs it.
Because negotiation doesn't complete, it restarts discovery every ~10 s.
(EPRI's reference UCM uses the same ~100 ms delay, so it may hit the same
issue.)

Next test: `--app-reply-delay` now defaults to 1.0 s. If it still fails, try
`--max-payload-indicator 0x00` and then `--max-payload-indicator nak`.
