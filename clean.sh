#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

dotnet clean src/Spice86.sln
find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
