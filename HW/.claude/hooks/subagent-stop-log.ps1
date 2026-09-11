# SubagentStop, async: record every delegation so the multi-agent economics question can be answered (MA-2).
# Without these numbers there is no way to answer "was splitting off a subagent worth it?".
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput
Write-Jsonl -File 'subagents.jsonl' -Record @{
    event      = 'subagent_stop'
    session_id = [string](Get-Prop $in 'session_id' '')
    agent      = [string](Get-Prop $in 'agent_type' (Get-Prop $in 'subagent_type' ''))
    name       = [string](Get-Prop $in 'agent_name' '')
}
exit 0
