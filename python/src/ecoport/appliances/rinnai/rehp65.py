"""Rinnai REHP65 heat pump water heater support.

Wraps the generic CTA-2045 API with Rinnai-specific capability mappings.
"""


class REHP65:
    """High-level interface to a Rinnai REHP65 over CTA-2045.

    TODO: implement get_temperature/get_setpoint/get_operating_state/
    get_energy/get_capabilities/set_setpoint/shed/end_shed per
    docs/protocol.md once confirmed.
    """
