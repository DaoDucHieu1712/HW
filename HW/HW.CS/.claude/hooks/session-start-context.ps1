# SessionStart -> stdout/additionalContext DUOC dua vao context cua model (HR-5).
# Muc dich: mo phien voi du lieu THAT (nhanh, diff, task dang mo), khong bat model di hoi.
. "$PSScriptRoot/lib/Common.ps1"
$null = Read-HookInput

$root  = Get-ProjectDir
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('## Trang thai workspace (do SessionStart hook nap)')

$branch = (Invoke-Quietly "git -C ""$root"" rev-parse --abbrev-ref HEAD").Output.Trim()
if ($branch) { $lines.Add("- Nhanh: $branch") }

$dirty = (Invoke-Quietly "git -C ""$root"" status --porcelain").Output.Trim()
if ($dirty) {
    $rows = $dirty -split "`r?`n"
    $lines.Add("- Working tree: $($rows.Count) file thay doi")
    $lines.Add('~~~')
    $lines.Add((($rows | Select-Object -First 20) -join "`n"))
    $lines.Add('~~~')
} else {
    $lines.Add('- Working tree: sach')
}

$task = Join-Path $root '.claude\state\CURRENT_TASK.md'
if (Test-Path $task) {
    $lines.Add('')
    $lines.Add('### Task dang mo (.claude/state/CURRENT_TASK.md)')
    $lines.Add((Get-Content $task -Raw -Encoding utf8))
}

$run = Join-Path $root '.claude\state\loop-run.json'
if (Test-Path $run) {
    try {
        $r = Get-Content $run -Raw -Encoding utf8 | ConvertFrom-Json
        $lines.Add('')
        $lines.Add("### Loop dang chay: $($r.workflow) | buoc: $($r.step) | turn: $($r.turns)/$($r.maxTurns)")
    } catch { }
}

Add-Context -Text ($lines -join "`n") -EventName 'SessionStart'
