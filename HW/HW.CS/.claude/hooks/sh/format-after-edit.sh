#!/usr/bin/env bash
# Ban POSIX cua format-after-edit.ps1. PostToolUse matcher "Edit|Write", nen dat async:true.
set -euo pipefail
payload=$(cat)
file=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // ""')
[ -z "$file" ] || [ ! -f "$file" ] && exit 0

case "$file" in
  *.cs)
    dir=$(dirname "$file")
    while [ "$dir" != "/" ] && [ "$dir" != "." ]; do
      proj=$(find "$dir" -maxdepth 1 -name '*.csproj' 2>/dev/null | head -1)
      [ -n "$proj" ] && break
      dir=$(dirname "$dir")
    done
    if [ -n "${proj:-}" ]; then
      rel=${file#"$(dirname "$proj")"/}
      dotnet format "$proj" --include "$rel" --verbosity quiet --no-restore || {
        echo "dotnet format bao loi tren $rel" >&2; exit 1; }
    fi
    ;;
  *.ts|*.tsx|*.js|*.jsx|*.json|*.css|*.md)
    [ -f package.json ] && npx --no-install prettier --write "$file" >/dev/null 2>&1 || true
    ;;
esac
exit 0
