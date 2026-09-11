# PostToolUse matcher "*", async:true -> audit every action into .claude/state/audit.jsonl
# To ship it to a central compliance system: switch to { "type": "http", "url": "..." } in settings.json (HR-7).
# Only records a TRACE (tool name + the main argument), never file contents -> keeps secrets out of the log.
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
