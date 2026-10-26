#!/usr/bin/env bash
set -euo pipefail

port="${1:-5555}"
if ! [[ "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
  echo "Usage: $0 [udp-port]" >&2
  exit 2
fi

if ! command -v ffplay >/dev/null 2>&1; then
  echo "ffplay is required. Install an FFmpeg build with RTP support." >&2
  exit 1
fi

echo "Listening for the XREAL composite on rtp://0.0.0.0:${port}"
echo "Left half: actual XR left eye. Right half: optional RGB doctor view."
exec ffplay \
  -hide_banner \
  -loglevel warning \
  -fflags nobuffer \
  -flags low_delay \
  -framedrop \
  "rtp://0.0.0.0:${port}"
