# SubagentStop, async: ghi lai moi lan uy thac de tra loi cau hoi kinh te cua multi-agent (MA-2).
# Khong co so lieu nay thi khong tra loi duoc "tach subagent co dang khong?".
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput
Write-Jsonl -File 'subagents.jsonl' -Record @{
    event      = 'subagent_stop'
    session_id = [string](Get-Prop $in 'session_id' '')
    agent      = [string](Get-Prop $in 'agent_type' (Get-Prop $in 'subagent_type' ''))
    name       = [string](Get-Prop $in 'agent_name' '')
}
exit 0
