# Getting Started

## Prerequisites

- Python 3.11+
- [PlatformIO](https://platformio.org/) (for firmware work)
- An isolated USB/RS-485 adapter (for talking to real hardware)
- EPRI's UCM simulator app/software (optional but recommended — lets you
  develop and test the protocol implementation without appliance hardware)

## Python environment

```bash
cd python
python -m venv .venv
. .venv/Scripts/activate   # Windows PowerShell: .venv\Scripts\Activate.ps1
pip install -e ".[dev]"
pytest
```

## Firmware environment

```bash
cd firmware
pio run
```

## Tools

Command-line helpers live in `tools/`:

- `sniff.py` — passively capture and log raw CTA-2045 traffic. It never
  writes to the serial port:

  ```powershell
  python tools\sniff.py --port COM3 --scenario idle-baseline --duration 300
  ```

  Each run creates a timestamped capture directory under `captures\rinnai\`
  containing `capture.bin` (the original byte stream), `capture.jsonl`
  (timestamped hex/base64 receive events), and a `notes.md` operator log.
- `decode.py` — decode a saved capture (RX frames, ACK/NAKs, and any
  `transmit.jsonl` TX log) into a labelled timeline:

  ```powershell
  python tools\decode.py captures\rinnai\<capture-dir>
  ```
- `ucm.py` — **transmits.** Acts as a minimal CTA-2045 UCM: answers the
  appliance with link ACK/NAKs and, with `--probe`, starts the handshake and
  sends read-only queries (see "First active session" below).
- `query.py` — send a one-off query to a connected appliance (not yet implemented)
- `interactive.py` — interactive REPL for exploring the protocol (not yet implemented)
- `packet_capture.py` — capture helper shared by the above

Before using the sniffer on the heater, follow the
[Rinnai control-panel passive-capture plan](rinnai-control-panel-capture-plan.md).
It provides a repeatable manual test matrix for idle, mode transitions
(including Heat Pump Only if available), setpoint changes, and heating
transitions — all without sending any CTA-2045 command.

## First active session (`ucm.py`)

`ucm.py` refuses to run without `--transmit`. It only ever sends:

| When | Bytes | Meaning |
|---|---|---|
| Any packet received | `06 00` / `15 xx` | Link ACK / NAK, ~60 ms later |
| Appliance asks max payload | `08 03 00 02 19 07 ..` | "We accept up to 256 bytes" (Level 2 minimum) |
| `--probe` start | `08 01/02/03 00 00 ..` | Message Type Supported Queries |
| `--probe` start + every 60 s | `08 01 00 02 0E 01 ..` | Outside comm status: good |
| `--probe` start + every 60 s | `08 01 00 02 12 00 ..` | Query operational state |
| `--probe` start | `08 02 00 02 01 01 ..` | Intermediate DR GetInformation |

No shed, load-up, setpoint, price, or other control commands.

```powershell
python tools\ucm.py --port COM4 --scenario first-ucm-probe --transmit --probe --duration 300
```

Look for `RX 06 00 ... link ACK` after each TX — that is the heater
answering. Output goes to a capture directory with `capture.bin` (RX only),
`transmit.jsonl` (our TX plus any adapter echo), and `session.log`.

If every TX gets "no ACK": check D+/D- polarity first, then whether the
adapter needs `--rts-tx` (adapters without automatic direction control).
`--port loop://` runs a hardware-free smoke test.

## Safety note

The CTA-2045 AC-form-factor connector can carry mains power in addition to
RS-485. Only pins 1 (D-), 7 (D+), and 8 (GND) are used for this project;
leave all other pins unconnected and use an isolated RS-485 adapter.
