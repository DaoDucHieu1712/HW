#!/usr/bin/env bash
# Ban POSIX cua stop-quality-gate.ps1. Su kien Stop. exit 2 = KHONG cho dung (AG-5, AG-8).
set -uo pipefail
payload=$(cat)
[ "${LOOP_QUALITY_GATE:-0}" != "1" ] && exit 0
[ "$(printf '%s' "$payload" | jq -r '.stop_hook_active // false')" = "true" ] && exit 0

git status --porcelain | grep -Eq '\.(cs|ts|tsx|js|jsx|sql)$' || exit 0

build_cmd=${LOOP_BUILD_CMD:-"dotnet build --nologo -v q"}
if ! out=$(eval "$build_cmd" 2>&1); then
  echo "CHUA XONG - BUILD DO (hook Stop). Lenh: $build_cmd" >&2
  printf '%s\n' "$out" | grep -iE 'error' | head -25 >&2
  echo "Sua het loi build roi moi ket thuc luot." >&2
  exit 2
fi

test_cmd=${LOOP_TEST_CMD:-}
if [ -n "$test_cmd" ]; then
  if ! out=$(eval "$test_cmd" 2>&1); then
    echo "CHUA XONG - TEST DO (hook Stop). Lenh: $test_cmd" >&2
    printf '%s\n' "$out" | grep -iE 'failed|error' | head -25 >&2
    exit 2
  fi
fi
exit 0
