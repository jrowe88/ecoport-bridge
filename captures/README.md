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
    │   ├── capture.json
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
