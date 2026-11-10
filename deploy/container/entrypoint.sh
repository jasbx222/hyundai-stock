#!/bin/bash
# Starts Kestrel on loopback, waits until it accepts connections, then exposes
# it on $PUBLIC_PORT. Opening the public port only after the app is ready lets
# the Cloudflare Container class treat "port open" as "app ready".
set -euo pipefail

APP_PORT="${APP_PORT:-5169}"
PUBLIC_PORT="${PUBLIC_PORT:-8080}"
READY_TIMEOUT="${READY_TIMEOUT_SECONDS:-170}"

dotnet /app/HyundaiStock.Web.dll &
app_pid=$!

ready=0
for _ in $(seq 1 "$READY_TIMEOUT"); do
  if ! kill -0 "$app_pid" 2>/dev/null; then
    echo "hyundai-entrypoint: app exited during startup" >&2
    wait "$app_pid" || exit $?
    exit 1
  fi
  if (exec 3<>"/dev/tcp/127.0.0.1/${APP_PORT}") 2>/dev/null; then ready=1; break; fi
  sleep 1
done
if [ "$ready" -ne 1 ]; then
  echo "hyundai-entrypoint: app not listening on ${APP_PORT} after ${READY_TIMEOUT}s" >&2
  kill "$app_pid" 2>/dev/null || true
  exit 1
fi

echo "hyundai-entrypoint: app ready, exposing :${PUBLIC_PORT}"
socat "TCP-LISTEN:${PUBLIC_PORT},fork,reuseaddr" "TCP:127.0.0.1:${APP_PORT}" &
proxy_pid=$!

trap 'kill -TERM "$app_pid" "$proxy_pid" 2>/dev/null || true' TERM INT
# Exit (and let Cloudflare restart the instance) if either process dies.
wait -n "$app_pid" "$proxy_pid"
status=$?
kill -TERM "$app_pid" "$proxy_pid" 2>/dev/null || true
exit "$status"
