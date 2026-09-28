"""Enumerations for CTA-2045 message types, operating states, and DR states."""

from enum import Enum


class OperatingState(Enum):
    UNKNOWN = "unknown"
    RUNNING = "running"
    SHEDDING = "shedding"
    LOAD_UP = "load_up"
    CRITICAL_PEAK = "critical_peak"


class ShedState(Enum):
    NORMAL = "normal"
    SHED = "shed"
