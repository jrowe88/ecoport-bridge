# Captures

Recorded CTA-2045 protocol traffic, organized by source.

- `epri/` — captures against the EPRI UCM simulator (known-good protocol
  reference)
- `rinnai/` — captures from the real Rinnai REHP65 appliance

## Layout convention

```text
captures/
└── rinnai/
    ├── 2026-09-30-idle/
    │   ├── capture.bin
    │   ├── capture.jsonl
    │   └── notes.md
    │
    ├── 2026-09-30-heating/
    │   ├── capture.bin
    │   └── notes.md
    │
    └── 2026-10-01-setpoint-change/
        ├── capture.bin
        └── notes.md
```

Each capture directory should include a `notes.md` describing what was
physically happening on the appliance during the capture (see
`docs/reverse-engineering.md`).

`capture.bin` is the exact byte stream as received. `capture.jsonl` is a
newline-delimited sidecar that records the timestamp, byte offset, length,
hexadecimal representation, and Base64 representation of each receive event.
Do not alter `capture.bin`; the JSON Lines file and `notes.md` are the
human-readable context for decoding it later.
