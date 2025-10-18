#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${FFAAAS_API_URL:-http://localhost:8080}}"
API_KEY="${2:-${FFAAAS_API_TOKEN:-dev-editor-token}}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/tools/FfaasLite.AdminCli/FfaasLite.AdminCli.csproj"
SEED_DIR="${SCRIPT_DIR}/seed-data"

run_cli() {
  local args=("$@")
  echo "dotnet run --project ${PROJECT} -- --url ${BASE_URL} --api-key ****** ${args[*]}" >&2
  dotnet run --project "${PROJECT}" -- \
    --url "${BASE_URL}" \
    --api-key "${API_KEY}" \
    "${args[@]}"
}

echo "Seeding demo flags against ${BASE_URL}..."

run_cli flags upsert new-ui \
  --type boolean \
  --bool-value false \
  --rules "${SEED_DIR}/new-ui.json"

run_cli flags upsert checkout \
  --type boolean \
  --bool-value false \
  --rules "${SEED_DIR}/checkout.json"

run_cli flags upsert ui-ver \
  --type string \
  --string-value v1 \
  --rules "${SEED_DIR}/ui-ver.json"

run_cli flags upsert rate-limit \
  --type number \
  --number-value 50 \
  --rules "${SEED_DIR}/rate-limit.json"

run_cli flags list

echo "Demo seed completed."
