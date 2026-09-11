#!/usr/bin/env bash
# Ban POSIX cua block-dangerous.ps1 (macOS/Linux). Yeu cau: jq.
# HR-8: LUON trich truong bang jq, LUON dong ngoac kep bien, KHONG BAO GIO eval.
set -euo pipefail
payload=$(cat)
cmd=$(printf '%s' "$payload" | jq -r '.tool_input.command // ""')
[ -z "$cmd" ] && exit 0

deny() {
  jq -n --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"deny",
      permissionDecisionReason:("Chan boi chinh sach repo: " + $r)}}'
  exit 0
}
ask() {
  jq -n --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"request",
      permissionDecisionReason:("Hanh dong kho hoan tac: " + $r)}}'
  exit 0
}

printf '%s' "$cmd" | grep -Eq 'rm[[:space:]]+-rf[[:space:]]+[/~$]'      && deny 'Xoa de quy pham vi rong.'
printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+push.*(--force([^-]|$)|[[:space:]]-f([[:space:]]|$))' && deny 'Force push ghi de lich su remote.'
printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+reset[[:space:]]+--hard'  && deny 'Vut bo thay doi chua commit.'
printf '%s' "$cmd" | grep -Eqi 'DROP[[:space:]]+(TABLE|DATABASE|SCHEMA)' && deny 'DDL pha huy du lieu.'
printf '%s' "$cmd" | grep -Eqi 'TRUNCATE[[:space:]]+TABLE'              && deny 'Xoa toan bo du lieu bang.'
printf '%s' "$cmd" | grep -Eqi 'DELETE[[:space:]]+FROM' && ! printf '%s' "$cmd" | grep -Eqi 'WHERE' && deny 'DELETE khong co WHERE.'
printf '%s' "$cmd" | grep -Eq '(cat|type)[^|]*\.env([[:space:]]|$)'     && deny 'Doc file secret.'
printf '%s' "$cmd" | grep -Eq 'npm[[:space:]]+publish|dotnet[[:space:]]+nuget[[:space:]]+push' && deny 'Phat hanh goi ra ngoai.'

printf '%s' "$cmd" | grep -Eq 'dotnet[[:space:]]+ef[[:space:]]+database[[:space:]]+update' && ask 'Migration doi schema that.'
printf '%s' "$cmd" | grep -Eq '(kubectl|helm)[[:space:]]+(apply|delete|rollout)'            && ask 'Thay doi cum Kubernetes.'

exit 0
