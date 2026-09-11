<#
.SYNOPSIS
  Reads .claude/state/*.jsonl and prints the loop's three operational metrics.

.DESCRIPTION
  These three metrics matter more than "accuracy" (AI-03, AG-12):

    1. Turns per completed task (p50/p95)  -> a spike means the model is flailing,
                                              which means the TOOL or the PROMPT is broken, not the model.
    2. Cost per completed task             -> the only economic metric worth tracking.
                                              NOT cost per request.
    3. Tool error rate PER tool            -> the tool that errors often is the one with a bad description/schema.

  This template cannot read token usage from the transcript, so the cost column is empty -
  wire it into your gateway (see .claude/llms/README.md) to get real numbers.

.EXAMPLE
  ./Trace.ps1
  ./Trace.ps1 -Top 15
#>
[CmdletBinding()]
param([int]$Top = 10)

$ErrorActionPreference = 'SilentlyContinue'
$root  = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$state = Join-Path $root '.claude\state'

function Read-Jsonl([string]$Name) {
    $p = Join-Path $state $Name
    if (-not (Test-Path $p)) { return @() }
    return Get-Content $p -Encoding utf8 | Where-Object { $_ } | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { }
    }
}

$audit = @(Read-Jsonl 'audit.jsonl')
$subs  = @(Read-Jsonl 'subagents.jsonl')

Write-Output '=== LOOP TRACE ==='
if ($audit.Count -eq 0) {
    Write-Output 'No data yet. The audit-log.ps1 hook creates .claude/state/audit.jsonl as you work.'
    exit 0
}

# --- 1. Volume per tool ---
Write-Output ''
Write-Output "-- Most called tools (top $Top) --"
$audit | Where-Object { $_.event -eq 'tool' } |
    Group-Object tool | Sort-Object Count -Descending | Select-Object -First $Top |
    ForEach-Object { '{0,-22} {1,5}' -f $_.Name, $_.Count } | Write-Output

# --- 2. Blocked actions ---
$blocked = @($audit | Where-Object { $_.event -eq 'blocked' })
Write-Output ''
Write-Output "-- Blocked by hooks: $($blocked.Count) times --"
$blocked | Group-Object reason | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output

# --- 3. Thrashing: the EARLIEST signal that the loop is broken ---
$thrash = @($audit | Where-Object { $_.event -eq 'thrashing' })
Write-Output ''
Write-Output "-- Thrashing (same tool + same args repeated): $($thrash.Count) times --"
$thrash | Group-Object tool | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output
if ($thrash.Count -gt 0) {
    Write-Output '   => Re-read these tools descriptions. A tool that errors often is a tool with a bad description/schema (AG-8).'
}

# --- 4. Multi-agent economics ---
Write-Output ''
Write-Output "-- Subagent delegations: $($subs.Count) --"
$subs | Group-Object agent | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output
if ($subs.Count -gt 0) {
    Write-Output '   => Every subagent is a separate context that must reload the project context (MA-2).'
    Write-Output '      If one agent type is called a lot for SHORT work, it is a loss: doing it inline in the main thread is cheaper.'
}

# --- 5. Closed runs ---
$runs = Join-Path $state 'runs'
if (Test-Path $runs) {
    $all = Get-ChildItem $runs -Filter *.json | ForEach-Object {
        try { Get-Content $_.FullName -Raw -Encoding utf8 | ConvertFrom-Json } catch { }
    }
    if ($all) {
        Write-Output ''
        Write-Output "-- Closed loops: $($all.Count) --"
        $turns  = @($all | ForEach-Object { [int]$_.turns } | Sort-Object)
        $p50    = $turns[[int][math]::Floor($turns.Count * 0.5)]
        $p95i   = [int][math]::Floor($turns.Count * 0.95); if ($p95i -ge $turns.Count) { $p95i = $turns.Count - 1 }
        $done   = @($all | Where-Object { $_.outcome -eq 'done' }).Count
        Write-Output ("Turns per task: p50={0}  p95={1}" -f $p50, $turns[$p95i])
        Write-Output ("Completion rate: {0}/{1}" -f $done, $all.Count)
        Write-Output ("Budget ceiling hit: {0}" -f @($all | Where-Object { [int]$_.turns -ge [int]$_.maxTurns }).Count)
        Write-Output '   => Hitting the ceiling often = the ceiling is wrong OR the agent is stuck. Read the trajectory, not the final answer.'
    }
}
