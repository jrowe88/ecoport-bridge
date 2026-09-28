#!/usr/bin/env bash
set -euo pipefail

# Builds and flashes the EcoPort Bridge firmware via PlatformIO.

cd "$(dirname "$0")/../firmware"
pio run --target upload
