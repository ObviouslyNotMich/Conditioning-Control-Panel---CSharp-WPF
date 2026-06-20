#!/usr/bin/env bash
set -euo pipefail

# Publish the Avalonia Linux desktop app as a self-contained folder.
# Run this on a Linux machine or WSL.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj"
RUNTIME="${1:-linux-x64}"
CONFIG="${2:-Release}"
OUTPUT="$SCRIPT_DIR/publish/$RUNTIME"

dotnet publish "$PROJECT" \
    -c "$CONFIG" \
    -r "$RUNTIME" \
    --self-contained true \
    -o "$OUTPUT"

echo "Published to $OUTPUT"
