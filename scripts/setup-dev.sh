#!/usr/bin/env bash
set -euo pipefail

# Sets up a local Python development environment for EcoPort Bridge.

cd "$(dirname "$0")/../python"
python -m venv .venv
source .venv/bin/activate
pip install --upgrade pip
pip install -e ".[dev]"

echo "Done. Activate with: source python/.venv/bin/activate"
