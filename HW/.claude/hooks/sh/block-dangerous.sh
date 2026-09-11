#!/usr/bin/env bash
# POSIX version of block-dangerous.ps1 (macOS/Linux). Requires: jq.
# HR-8: ALWAYS extract fields with jq, ALWAYS quote variables, NEVER eval.
set -euo pipefail
payload=$(cat)
cmd=$(printf '%s' "$payload" | jq -r '.tool_input.command // ""')
[ -z "$cmd" ] && exit 0

deny() {
  jq -n --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"deny",
      permissionDecisionReason:("Blocked by repo policy: " + $r)}}'
  exit 0
}
ask() {
  jq -n --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"request",
      permissionDecisionReason:("Hard-to-undo action: " + $r)}}'
  exit 0
}

printf '%s' "$cmd" | grep -Eq 'rm[[:space:]]+-rf[[:space:]]+[/~$]'      && deny 'Wide-scope recursive delete.'
printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+push.*(--force([^-]|$)|[[:space:]]-f([[:space:]]|$))' && deny 'A force push overwrites remote history.'
printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+reset[[:space:]]+--hard'  && deny 'Discards uncommitted changes.'
printf '%s' "$cmd" | grep -Eqi 'DROP[[:space:]]+(TABLE|DATABASE|SCHEMA)' && deny 'Data-destroying DDL.'
printf '%s' "$cmd" | grep -Eqi 'TRUNCATE[[:space:]]+TABLE'              && deny 'Deletes every row in the table.'
printf '%s' "$cmd" | grep -Eqi 'DELETE[[:space:]]+FROM' && ! printf '%s' "$cmd" | grep -Eqi 'WHERE' && deny 'DELETE with no WHERE clause.'
printf '%s' "$cmd" | grep -Eq '(cat|type)[^|]*\.env([[:space:]]|$)'     && deny 'Reading a secrets file.'
printf '%s' "$cmd" | grep -Eq 'npm[[:space:]]+publish|dotnet[[:space:]]+nuget[[:space:]]+push' && deny 'Publishing a package externally.'

printf '%s' "$cmd" | grep -Eq 'dotnet[[:space:]]+ef[[:space:]]+database[[:space:]]+update' && ask 'A migration changes a real schema.'
printf '%s' "$cmd" | grep -Eq '(kubectl|helm)[[:space:]]+(apply|delete|rollout)'            && ask 'Changes a Kubernetes cluster.'

exit 0
