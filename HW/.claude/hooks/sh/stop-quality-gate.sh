#!/usr/bin/env bash
# POSIX version of stop-quality-gate.ps1. The Stop event. exit 2 = do NOT allow stopping (AG-5, AG-8).
set -uo pipefail
payload=$(cat)
[ "${LOOP_QUALITY_GATE:-0}" != "1" ] && exit 0
[ "$(printf '%s' "$payload" | jq -r '.stop_hook_active // false')" = "true" ] && exit 0

git status --porcelain | grep -Eq '\.(cs|ts|tsx|js|jsx|sql)$' || exit 0

build_cmd=${LOOP_BUILD_CMD:-"dotnet build --nologo -v q"}
if ! out=$(eval "$build_cmd" 2>&1); then
  echo "NOT DONE - BUILD IS RED (Stop hook). Command: $build_cmd" >&2
  printf '%s\n' "$out" | grep -iE 'error' | head -25 >&2
  echo "Fix every build error before ending the turn." >&2
  exit 2
fi

test_cmd=${LOOP_TEST_CMD:-}
if [ -n "$test_cmd" ]; then
  if ! out=$(eval "$test_cmd" 2>&1); then
    echo "NOT DONE - TESTS ARE RED (Stop hook). Command: $test_cmd" >&2
    printf '%s\n' "$out" | grep -iE 'failed|error' | head -25 >&2
    exit 2
  fi
fi
exit 0
