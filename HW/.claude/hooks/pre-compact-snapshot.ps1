# PreCompact, async: snapshot the state before the context gets compacted.
# Why it is needed: after compaction the details are gone. If a loop is running, we want ONE record
# outside the context so a later session (or a human reading the log) knows where it was (PE-8).
. "$PSScriptRoot/lib/Common.ps1"
$in   = Read-HookInput
$root = Get-ProjectDir

$branch = (Invoke-Quietly "git -C ""$root"" rev-parse --abbrev-ref HEAD").Output.Trim()
$dirty  = (Invoke-Quietly "git -C ""$root"" status --porcelain").Output.Trim()

Write-Jsonl -File 'compaction.jsonl' -Record @{
    event   = 'pre_compact'
    trigger = [string](Get-Prop $in 'trigger' '')
    branch  = $branch
    changed = if ($dirty) { ($dirty -split "`r?`n").Count } else { 0 }
}
exit 0
