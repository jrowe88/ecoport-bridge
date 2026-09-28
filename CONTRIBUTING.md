# Contributing to EcoPort Bridge

Thanks for your interest in this project! It's an early-stage hobby
reverse-engineering and hardware project, so contribution norms are
intentionally lightweight.

## Ground rules

- **Facts vs. hypotheses.** When documenting protocol behavior in
  `docs/protocol.md` or capture notes, clearly separate *confirmed* facts
  (from EPRI documentation or verified captures) from *hypotheses* (educated
  guesses from observed traffic). Don't let a guess silently become "the
  spec".
- **Generic vs. appliance-specific.** Keep the CTA-2045 protocol
  implementation (`python/src/ecoport/cta2045`,
  `firmware/src/cta2045`) free of Rinnai-specific assumptions. Appliance
  quirks belong under `appliances/rinnai` (Python) or `appliance/rinnai`
  (firmware).
- **Captures are data.** If you record new protocol traffic, add it under
  `captures/<appliance>/<date>-<scenario>/` with a `notes.md` describing what
  was happening on the appliance at the time.
- **Safety first.** The CTA-2045 AC interface is mains-adjacent. Any hardware
  changes involving the connector should call out isolation and safety
  considerations explicitly.

## Getting started

See `docs/getting-started.md` and `scripts/setup-dev.sh` for environment
setup, and `docs/architecture.md` for a tour of the codebase.

## Submitting changes

1. Open an issue describing the bug/feature/observation before larger changes.
2. Keep pull requests focused and small where possible.
3. Include capture data or protocol notes for any reverse-engineering claims.
