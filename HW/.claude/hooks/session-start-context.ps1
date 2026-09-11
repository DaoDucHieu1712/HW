# SessionStart -> stdout/additionalContext IS fed into the model's context (HR-5).
# Purpose: open the session with REAL data (branch, diff, the open task) instead of making the model ask.
. "$PSScriptRoot/lib/Common.ps1"
$null = Read-HookInput

$root  = Get-ProjectDir
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('## Workspace state (loaded by the SessionStart hook)')

$branch = (Invoke-Quietly "git -C ""$root"" rev-parse --abbrev-ref HEAD").Output.Trim()
if ($branch) { $lines.Add("- Branch: $branch") }

$dirty = (Invoke-Quietly "git -C ""$root"" status --porcelain").Output.Trim()
if ($dirty) {
    $rows = $dirty -split "`r?`n"
    $lines.Add("- Working tree: $($rows.Count) changed files")
    $lines.Add('~~~')
    $lines.Add((($rows | Select-Object -First 20) -join "`n"))
    $lines.Add('~~~')
} else {
    $lines.Add('- Working tree: clean')
}

$task = Join-Path $root '.claude\state\CURRENT_TASK.md'
if (Test-Path $task) {
    $lines.Add('')
    $lines.Add('### Open task (.claude/state/CURRENT_TASK.md)')
    $lines.Add((Get-Content $task -Raw -Encoding utf8))
}

$run = Join-Path $root '.claude\state\loop-run.json'
if (Test-Path $run) {
    try {
        $r = Get-Content $run -Raw -Encoding utf8 | ConvertFrom-Json
        $lines.Add('')
        $lines.Add("### Loop running: $($r.workflow) | step: $($r.step) | turn: $($r.turns)/$($r.maxTurns)")
    } catch { }
}

Add-Context -Text ($lines -join "`n") -EventName 'SessionStart'
