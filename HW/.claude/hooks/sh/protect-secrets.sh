#!/usr/bin/env bash
# POSIX version of protect-secrets.ps1. PreToolUse matcher "Read|Grep|Glob".
set -euo pipefail
payload=$(cat)
path=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // .tool_input.path // ""')
[ -z "$path" ] && exit 0

if printf '%s' "$path" | grep -Eq '(/\.env($|\.)|/secrets?/|appsettings\.Production\.json$|\.(pfx|p12|pem|key)$|/id_rsa|\.aws/credentials|\.npmrc$)'; then
  jq -n --arg p "$path" '{hookSpecificOutput:{hookEventName:"PreToolUse",
      permissionDecision:"deny",
      permissionDecisionReason:("This path holds secrets and is blocked by repo policy: " + $p
        + ". If you need the configuration, read the key NAMES in appsettings.Development.json or ask the user.")}}'
  exit 0
fi
exit 0
