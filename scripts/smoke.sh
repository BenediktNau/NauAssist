#!/usr/bin/env bash
# NauAssist smoke test — wartet auf /health und prüft den Status.
# Aufruf:
#   scripts/smoke.sh                              # default: http://localhost:8080
#   scripts/smoke.sh http://localhost:8080
#   TIMEOUT_SECONDS=120 scripts/smoke.sh
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-60}"
HEALTH_URL="${BASE_URL%/}/health"

echo "Warte auf ${HEALTH_URL} (max ${TIMEOUT_SECONDS}s) …"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
last_response=""
while [ "$(date +%s)" -lt "$deadline" ]; do
  if last_response="$(curl --silent --show-error --fail "${HEALTH_URL}" 2>/dev/null)"; then
    echo "OK ${HEALTH_URL}"
    if command -v jq >/dev/null 2>&1; then
      echo "${last_response}" | jq '{status, entries: (.entries | map_values(.status))}'
    else
      echo "${last_response}"
    fi
    exit 0
  fi
  sleep 2
done

echo "FEHLER: ${HEALTH_URL} antwortete nicht innerhalb von ${TIMEOUT_SECONDS}s." >&2
if [ -n "${last_response}" ]; then
  echo "Letzte Antwort:" >&2
  echo "${last_response}" >&2
fi
exit 1
