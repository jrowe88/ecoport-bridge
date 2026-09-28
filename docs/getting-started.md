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

- `sniff.py` — passively capture and log raw CTA-2045 traffic
- `decode.py` — decode a saved capture into human-readable messages
- `query.py` — send a one-off query to a connected appliance
- `interactive.py` — interactive REPL for exploring the protocol
- `packet_capture.py` — capture helper shared by the above

## Safety note

The CTA-2045 AC-form-factor connector can carry mains power in addition to
RS-485. Only pins 1 (D-), 7 (D+), and 8 (GND) are used for this project;
leave all other pins unconnected and use an isolated RS-485 adapter.
