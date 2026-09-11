# PreToolUse matcher "Bash": the repo's DETERMINISTIC boundary (AI-05 HR-7).
# The hook runs even when the model "forgets", and it CANNOT be talked around by prompt injection -
# because it never reads the prompt.
# Safety principle (HR-8): always extract fields with a JSON parser, never Invoke-Expression.
. "$PSScriptRoot/lib/Common.ps1"
$in  = Read-HookInput
$cmd = [string](Get-Prop $in 'tool_input.command' '')
if (-not $cmd) { exit 0 }

# --- DENY: destructive / irreversible / secret-leaking ---
$deny = @(
    @{ Rx = '(?i)rm\s+-rf\s+[/\x5C]\s*$';                 Why = 'Recursive delete from the root directory.' },
    @{ Rx = '(?i)rm\s+-rf\s+[~$]';                         Why = 'Recursive delete of home / an environment variable.' },
    @{ Rx = '(?i)\bgit\s+push\b.*--force(?!-with-lease)';  Why = 'A force push overwrites remote history. Use --force-with-lease.' },
    @{ Rx = '(?i)\bgit\s+push\b.*\s-f(\s|$)';              Why = 'A force push overwrites remote history.' },
    @{ Rx = '(?i)\bgit\s+reset\s+--hard\b';                Why = 'Discards uncommitted changes. Commit or stash first.' },
    @{ Rx = '(?i)\bgit\s+clean\s+-[a-z]*f';                Why = 'Deletes untracked files, irreversibly.' },
    @{ Rx = '(?i)\bDROP\s+(TABLE|DATABASE|SCHEMA)\b';      Why = 'Data-destroying DDL.' },
    @{ Rx = '(?i)\bTRUNCATE\s+TABLE\b';                    Why = 'Deletes every row in the table.' },
    # [\s\S] is ONE character class, not an alternation (.|\n).
    # (.|\n)* inside a lookahead causes catastrophic backtracking: this hook sits on the
    # PreToolUse path of every Bash command, and hanging here hangs the whole session.
    @{ Rx = '(?i)\bDELETE\s+FROM\b(?![\s\S]*\bWHERE\b)';   Why = 'DELETE without a WHERE clause.' },
    @{ Rx = '(?i)\bUPDATE\b[\s\S]+\bSET\b(?![\s\S]*\bWHERE\b)'; Why = 'UPDATE without a WHERE clause.' },
    @{ Rx = '(?i)\b(cat|type|Get-Content)\b[^|]*\.env(\s|$)';   Why = 'Reading a secrets file.' },
    @{ Rx = '(?i)\bnpm\s+publish\b';                       Why = 'Publishing a package externally must be done by a human.' },
    @{ Rx = '(?i)\bdotnet\s+nuget\s+push\b';               Why = 'Publishing a package externally must be done by a human.' }
)
foreach ($r in $deny) {
    if (Test-Pattern -Text $cmd -Pattern $r.Rx) {
        Write-Jsonl -File 'audit.jsonl' -Record @{ event = 'blocked'; tool = 'Bash'; summary = $cmd; reason = $r.Why }
        Deny-Tool "Blocked by repo policy (.claude/hooks/block-dangerous.ps1): $($r.Why) If this is genuinely needed, the user must run the command themselves."
    }
}

# --- ASK: legitimate but hard to undo (AG-11) ---
$ask = @(
    @{ Rx = '(?i)\bdotnet\s+ef\s+database\s+update\b';       Why = 'A migration changes a real schema.' },
    @{ Rx = '(?i)\b(kubectl|helm)\s+(apply|delete|rollout)\b'; Why = 'Changes a Kubernetes cluster.' },
    @{ Rx = '(?i)\bdocker\s+(push|system\s+prune)\b';        Why = 'A Docker operation with effects outside this machine.' }
)
foreach ($r in $ask) {
    if (Test-Pattern -Text $cmd -Pattern $r.Rx) { Ask-Tool "Hard-to-undo action: $($r.Why) It needs the user's confirmation." }
}

exit 0   # no decision -> the normal permission flow continues
