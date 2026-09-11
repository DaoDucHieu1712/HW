#!/usr/bin/env bash
# Ban POSIX cua protect-secrets.ps1. PreToolUse matcher "Read|Grep|Glob".
set -euo pipefail
payload=$(cat)
path=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // .tool_input.path // ""')
[ -z "$path" ] && exit 0

if printf '%s' "$path" | grep -Eq '(/\.env($|\.)|/secrets?/|appsettings\.Production\.json$|\.(pfx|p12|pem|key)$|/id_rsa|\.aws/credentials|\.npmrc$)'; then
  jq -n --arg p "$path" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"deny",
      permissionDecisionReason:("Duong dan chua bi mat, bi chan boi chinh sach repo: " + $p
        + ". Neu can cau hinh, doc TEN key trong appsettings.Development.json hoac hoi nguoi dung.")}}'
  exit 0
fi
exit 0
