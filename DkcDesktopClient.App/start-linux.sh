#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DLL="$SCRIPT_DIR/DkcDesktopClient.App.dll"
APP_BIN="$SCRIPT_DIR/DkcDesktopClient.App"

if [[ -f "$APP_DLL" ]]; then
  exec dotnet "$APP_DLL" "$@"
fi

if [[ -x "$APP_BIN" ]]; then
  exec "$APP_BIN" "$@"
fi

echo "Fehler: Weder '$APP_DLL' noch '$APP_BIN' gefunden." >&2
echo "Bitte zuerst die App bauen (dotnet build/publish)." >&2
exit 1

