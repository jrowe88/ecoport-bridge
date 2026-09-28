# Architecture

EcoPort Bridge is organized in five conceptual layers, from the appliance
electrical interface up to home-automation integrations:

```text
                    ECOport Bridge
                         │
          ┌──────────────┴──────────────┐
          │                             │
     CTA-2045 core                 Appliance layer
          │                             │
          │                    ┌────────┴─────────┐
          │                    │                  │
          │                 Rinnai            Other SGD
          │                    │
          └───────────┬────────┘
                      │
                  Transport
                      │
                 RS-485 / USB
```

## Layers

1. **Transport** — physical RS-485 UART, isolated transceiver, framing at the
   byte level. See `python/src/ecoport/transport/` and
   `firmware/src/transport/`.
2. **CTA-2045 core (protocol)** — generic CTA-2045 link layer, Basic DR and
   Intermediate message types, CRC, enums. No appliance-specific knowledge.
   See `python/src/ecoport/cta2045/` and `firmware/src/cta2045/`.
3. **Appliance layer** — appliance-specific capability mappings and quirks
   (e.g. Rinnai REHP65). See `python/src/ecoport/appliances/rinnai/` and
   `firmware/src/appliance/rinnai/`.
4. **Bridge / network** — device model, state aggregation, and the
   ESP32-based bridge networking stack. See `firmware/src/network/`.
5. **Integrations** — MQTT, Matter, and other outward-facing integrations.
   See `firmware/src/integrations/`.

## Why this separation?

Keeping the CTA-2045 protocol implementation free of Rinnai-specific
assumptions means a second EcoPort appliance (e.g. a different
manufacturer's CTA-2045 water heater) can be supported later by adding a new
`appliances/<vendor>` module, without touching the protocol core.
