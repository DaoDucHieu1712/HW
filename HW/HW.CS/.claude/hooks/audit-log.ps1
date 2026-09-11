# PostToolUse matcher "*", async:true -> kiem toan moi hanh dong vao .claude/state/audit.jsonl
# Muon day ve he thong tuan thu tap trung: doi sang { "type": "http", "url": "..." } trong settings.json (HR-7).
# Chi ghi DAU VET (ten tool + tham so chinh), khong ghi noi dung file -> tranh do secret vao log.
. "$PSScriptRoot/lib/Common.ps1"
if ($env:LOOP_AUDIT -eq '0') { exit 0 }
$in = Read-HookInput

$toolInput = Get-Prop $in 'tool_input' $null
$summary   = ''
foreach ($k in @('file_path', 'command', 'path', 'pattern', 'url', 'description')) {
    $v = [string](Get-Prop $toolInput $k '')
    if ($v) { $summary = "$k=$v"; break }
}
if ($summary.Length -gt 300) { $summary = $summary.Substring(0, 300) + '...' }

Write-Jsonl -File 'audit.jsonl' -Record @{
    event      = 'tool'
    session_id = [string](Get-Prop $in 'session_id' '')
    tool       = [string](Get-Prop $in 'tool_name' '')
    summary    = $summary
}
exit 0
