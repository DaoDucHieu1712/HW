# PreCompact, async: chup lai trang thai truoc khi context bi nen.
# Vi sao can: sau compaction, chi tiet da mat. Neu loop dang chay, ta muon con MOT ban ghi
# ngoai context de phien sau (hoac nguoi doc log) biet no dang o dau (PE-8).
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
