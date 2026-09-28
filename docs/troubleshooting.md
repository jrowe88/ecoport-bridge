# Troubleshooting

_A running list of gotchas. Add to this as they're discovered._

## No traffic observed on RS-485 lines

- Double-check pin mapping: only pins 1 (D-), 7 (D+), 8 (GND) on the
  `420B2V12FL0` connector are used. Verify polarity — D+/D- reversed is a
  common wiring mistake with RS-485.
- Confirm baud rate (19,200), 8 data bits, 1 stop bit, no parity.
- Confirm the isolated adapter is powered and enumerating correctly over
  USB.

## Garbled / inconsistent bytes

- Check termination resistor requirements for your specific RS-485 adapter
  and cable length.
- Verify a common ground reference between the adapter and the appliance
  connector (pin 8).

## Simulator behaves differently than the real appliance

- Expected to some degree — the EPRI simulator implements the generic
  CTA-2045 spec, while Rinnai's actual firmware may only implement a subset
  of commands or respond slightly differently. Record differences in
  `docs/protocol.md` under Observed/Hypotheses rather than treating the
  simulator as ground truth for appliance-specific behavior.
