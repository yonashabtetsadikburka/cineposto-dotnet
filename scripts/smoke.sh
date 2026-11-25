#!/usr/bin/env bash
# Local smoke test for the Docker setup.
#
# It builds the images, checks that the api refuses to start without a token, starts the api, runs the scraper on
# the saved fixtures (no network), and then calls the main endpoints with curl. Whatever happens, it removes the
# containers and the volume it created. It uses its own compose project name and an ephemeral admin token, so it
# never touches the data or the .env of a normal local run.
#
# Usage:  scripts/smoke.sh
#         SMOKE_API_PORT=19000 scripts/smoke.sh
#
# Needs: docker with the compose plugin, and curl.
#
# Exit codes:
#   0  all checks passed
#   10 docker, docker compose or curl is missing
#   11 image build failed
#   12 the api (or compose) did not refuse to start without ADMIN_TOKEN
#   13 the api did not become healthy
#   14 the scraper failed
#   20 /health                      21 /api/v1/cinemas
#   22 /api/v1/films                23 /api/v1/showings
#   30 the web frontend or one of its files is not served
#   31 the frontend tests are served by the api (they must not ship)
#   24 admin endpoint accepted a request without the token
#   25 admin endpoint rejected the right token or returned unexpected counts
#   26 a second scraper run changed the data
#   27 a failing source emptied the data or the scraper did not report the failure
set -uo pipefail

# Git Bash rewrites anything that looks like a path before calling docker.exe; this turns that off.
export MSYS_NO_PATHCONV=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Isolation: shell variables win over .env, so the run is the same on every machine.
export COMPOSE_PROJECT_NAME="${SMOKE_PROJECT:-cineposto-smoke}"
export API_PORT="${SMOKE_API_PORT:-18080}"
export SCRAPER_MODE=fixtures
export SCRAPER_FIXTURES_CASE=settings_valid
export FIXTURES_DIR=./tests/CinePosto.Connectors.Tests/Golden
export CINEPOSTO_DB_PATH=/data/cineposto.db
export SCRAPER_USER_AGENT=
export SCRAPER_HTTP_TIMEOUT_SECONDS=30

BASE="http://127.0.0.1:${API_PORT}"
EXPECTED_COUNTS='{"cinemas":1,"films":2,"showings":2}'
STARTED=$SECONDS
HTTP_STATUS=""
HTTP_BODY=""
SCRAPER_OUT=""
SCRAPER_RC=0

step() { echo; echo "==> $*"; }
pass() { echo "PASS  $*"; }

cleanup() {
  local rc=$?
  echo
  echo "==> cleaning up (docker compose down --volumes)"
  docker compose --profile jobs down --volumes --remove-orphans >/dev/null 2>&1 || true
  if [ "$rc" -eq 0 ]; then
    echo "SMOKE PASSED in $((SECONDS - STARTED))s"
  else
    echo "SMOKE FAILED (exit code $rc) after $((SECONDS - STARTED))s" >&2
  fi
  exit "$rc"
}
trap cleanup EXIT

# fail <exit code> <compose service whose logs to show, or empty> <message> [details]
fail() {
  local code=$1 service=$2 message=$3 details=${4:-}
  echo "FAIL  $message" >&2
  if [ -n "$details" ]; then
    echo "--- details ---" >&2
    echo "$details" >&2
  fi
  if [ -n "$service" ]; then
    echo "--- last logs of service '$service' ---" >&2
    docker compose --profile jobs logs --no-color --tail 100 "$service" >&2 || true
  fi
  exit "$code"
}

# get <path> [extra curl arguments]: sets HTTP_STATUS and HTTP_BODY.
# The status code is appended to the body by curl itself, so no temporary file is needed (with path conversion
# off, a native curl on Windows and the shell would not agree on where /tmp is).
get() {
  local path=$1
  shift
  local response
  response=$(curl -sS --max-time 10 -w $'\n%{http_code}' "$@" "${BASE}${path}" 2>/dev/null) || response=$'\n000'
  HTTP_STATUS=${response##*$'\n'}
  HTTP_BODY=${response%$'\n'*}
}

# expect_data <name> <exit code> <path> <text the body must contain>
expect_data() {
  local name=$1 code=$2 path=$3 needle=$4
  get "$path"
  [ "$HTTP_STATUS" = "200" ] || fail "$code" api "$name: expected 200 from $path, got $HTTP_STATUS" "$HTTP_BODY"
  case "$HTTP_BODY" in
    '' | '[]' | '{}') fail "$code" api "$name: $path returned no data" "$HTTP_BODY" ;;
  esac
  grep -qF -- "$needle" <<<"$HTTP_BODY" || fail "$code" api "$name: $path does not contain '$needle'" "$HTTP_BODY"
  pass "$name  (200, contains '$needle')"
}

run_scraper() {
  SCRAPER_OUT=$(docker compose run -T --rm scraper 2>&1)
  SCRAPER_RC=$?
}

generate_token() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
  else
    head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n'
  fi
}

step "0/10 prerequisites"
command -v docker >/dev/null 2>&1 || fail 10 "" "docker is not installed or not on PATH"
docker compose version >/dev/null 2>&1 || fail 10 "" "the docker compose plugin is not available"
command -v curl >/dev/null 2>&1 || fail 10 "" "curl is not installed or not on PATH"
ADMIN_TOKEN=$(generate_token)
export ADMIN_TOKEN
pass "docker, compose and curl found; project '${COMPOSE_PROJECT_NAME}', api on port ${API_PORT}"

step "1/10 build the images"
docker compose --profile jobs build || fail 11 "" "docker compose build failed"
pass "images built"

step "2/10 the api must refuse to start without a token"
API_IMAGE=$(docker compose config --images 2>/dev/null | grep -F 'cineposto-api' | head -n 1)
[ -n "$API_IMAGE" ] || API_IMAGE=cineposto-api:local
OUT=$(docker run --rm --pull=never "$API_IMAGE" 2>&1)
RC=$?
[ "$RC" -ne 0 ] || fail 12 "" "the api image started without ADMIN_TOKEN" "$OUT"
grep -q 'ADMIN_TOKEN' <<<"$OUT" || fail 12 "" "the api image exited ($RC) without explaining that ADMIN_TOKEN is missing" "$OUT"
pass "image exits with code $RC and says ADMIN_TOKEN is missing"
OUT=$(ADMIN_TOKEN='' docker compose config --quiet 2>&1)
RC=$?
[ "$RC" -ne 0 ] || fail 12 "" "docker compose accepted an empty ADMIN_TOKEN"
grep -q 'ADMIN_TOKEN' <<<"$OUT" || fail 12 "" "docker compose failed, but not because of ADMIN_TOKEN" "$OUT"
pass "docker compose rejects an empty ADMIN_TOKEN"

step "3/10 start the api and wait for the healthcheck"
docker compose up -d --wait --wait-timeout 120 api || fail 13 api "the api did not become healthy within 120s"
pass "api is healthy"

step "4/10 run the scraper on the saved fixtures"
run_scraper
echo "$SCRAPER_OUT"
[ "$SCRAPER_RC" -eq 0 ] || fail 14 api "the scraper exited with code $SCRAPER_RC" "$SCRAPER_OUT"
grep -qF 'films=2' <<<"$SCRAPER_OUT" || fail 14 api "the scraper did not report 2 films" "$SCRAPER_OUT"
pass "scraper finished with exit code 0"

step "5/10 public endpoints"
expect_data "/health" 20 /health "Healthy"
expect_data "cinemas" 21 /api/v1/cinemas "clarici-foligno"
expect_data "films" 22 /api/v1/films "Nebbia sul Lago"
expect_data "showings" 23 /api/v1/showings "clarici-foligno"

step "6/10 web frontend"
expect_data "web page" 30 / "js/app.js"
expect_data "web script" 30 /js/app.js "renderHome"
expect_data "italian texts" 30 /i18n/it.json '"nav.home"'
expect_data "english texts" 30 /i18n/en.json '"nav.home"'
get /tests/i18n.test.mjs
[ "$HTTP_STATUS" = "404" ] || fail 31 api "the frontend tests are served by the api (status $HTTP_STATUS)" "$HTTP_BODY"
pass "frontend tests are not shipped in the image"
step "7/10 admin endpoint"
get /api/v1/admin/dataset-info
[ "$HTTP_STATUS" = "401" ] || fail 24 api "admin endpoint without a token: expected 401, got $HTTP_STATUS" "$HTTP_BODY"
pass "no token -> 401"
get /api/v1/admin/dataset-info -H "X-Admin-Token: wrong-token-wrong-token"
[ "$HTTP_STATUS" = "401" ] || fail 24 api "admin endpoint with a wrong token: expected 401, got $HTTP_STATUS" "$HTTP_BODY"
pass "wrong token -> 401"
get /api/v1/admin/dataset-info -H "X-Admin-Token: ${ADMIN_TOKEN}"
[ "$HTTP_STATUS" = "200" ] || fail 25 api "admin endpoint with the right token: expected 200, got $HTTP_STATUS" "$HTTP_BODY"
[ "$HTTP_BODY" = "$EXPECTED_COUNTS" ] || fail 25 api "unexpected counts: wanted $EXPECTED_COUNTS" "$HTTP_BODY"
pass "right token -> 200 $HTTP_BODY"

step "8/10 a second scraper run must not change the data"
run_scraper
[ "$SCRAPER_RC" -eq 0 ] || fail 26 api "the second scraper run exited with code $SCRAPER_RC" "$SCRAPER_OUT"
get /api/v1/admin/dataset-info -H "X-Admin-Token: ${ADMIN_TOKEN}"
[ "$HTTP_BODY" = "$EXPECTED_COUNTS" ] || fail 26 api "counts changed after the second run: $HTTP_BODY" "$HTTP_BODY"
pass "counts unchanged: $HTTP_BODY"

step "9/10 a failing source must keep the last known showings"
SCRAPER_FIXTURES_CASE=movies_http500 run_scraper
echo "$SCRAPER_OUT"
[ "$SCRAPER_RC" -eq 1 ] || fail 27 api "the scraper should exit with code 1 when the source fails, got $SCRAPER_RC" "$SCRAPER_OUT"
get /api/v1/admin/dataset-info -H "X-Admin-Token: ${ADMIN_TOKEN}"
[ "$HTTP_BODY" = "$EXPECTED_COUNTS" ] || fail 27 api "the failing run changed the data: $HTTP_BODY" "$HTTP_BODY"
pass "scraper reported the failure (exit 1) and the data is still $HTTP_BODY"

step "10/10 the api is still healthy"
expect_data "/health" 20 /health "Healthy"
