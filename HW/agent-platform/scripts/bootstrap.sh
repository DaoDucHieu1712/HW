#!/usr/bin/env bash
# Create a virtual environment, install dependencies, and verify the platform.
set -euo pipefail

cd "$(dirname "$0")/.."

PYTHON=${PYTHON:-python3}
$PYTHON -m venv .venv
# shellcheck disable=SC1091
source .venv/bin/activate

python -m pip install --upgrade pip
python -m pip install -r requirements.txt

DOTENV=".env"
if [ ! -f "$DOTENV" ]; then
  cp env.example "$DOTENV"
  echo "created $DOTENV from env.example -- fill in the credentials before a live run"
fi

python scripts/healthcheck.py
python -m pytest -q
echo
echo "Ready.  Try:  python cli.py --dry-run run '/fixbug DEMO-1'"
