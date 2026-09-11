<#
.SYNOPSIS
  The loop's state machine: init / advance a step / show status / close.

.DESCRIPTION
  A loop needs state OUTSIDE the context, for three reasons:
    1. context gets compacted -> details disappear, files do not;
    2. a subagent starts cold -> it needs somewhere to read "where are we" (MA-6);
    3. stop conditions must be verifiable from the outside (AG-6).

  Writes .claude/state/loop-run.json and .claude/state/CURRENT_TASK.md.

.EXAMPLE
  ./Task.ps1 -Action init -Workflow dev-loop -Title "Add endpoint GET /blogs/{id}"
  ./Task.ps1 -Action step -Step implement
  ./Task.ps1 -Action status
  ./Task.ps1 -Action close -Outcome done
#>
[CmdletBinding()]
param(
    [ValidateSet('init', 'step', 'status', 'close')] [string]$Action = 'status',
    [string]$Workflow = 'dev-loop',
    [string]$Title    = '',
    [string]$Step     = '',
    [ValidateSet('done', 'blocked', 'abandoned')] [string]$Outcome = 'done',
    [int]$MaxTurns    = 0
)

$ErrorActionPreference = 'Stop'
$root  = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$state = Join-Path $root '.claude\state'
if (-not (Test-Path $state)) { New-Item -ItemType Directory -Path $state -Force | Out-Null }
$runFile = Join-Path $state 'loop-run.json'

function Load-Run {
    if (Test-Path $runFile) { return Get-Content $runFile -Raw -Encoding utf8 | ConvertFrom-Json }
    return $null
}
function Save-Run($r) { ($r | ConvertTo-Json -Depth 8) | Set-Content -Path $runFile -Encoding utf8 }

switch ($Action) {
    'init' {
        if ($MaxTurns -le 0) {
            $MaxTurns = if ($env:LOOP_MAX_TURNS) { [int]$env:LOOP_MAX_TURNS } else { 25 }
        }
        $run = [pscustomobject]@{
            id        = [guid]::NewGuid().ToString('N').Substring(0, 8)
            workflow  = $Workflow
            title     = $Title
            step      = 'gather'
            turns     = 0
            maxTurns  = $MaxTurns
            startedAt = (Get-Date).ToString('o')
            history   = @(@{ step = 'gather'; at = (Get-Date).ToString('o') })
            outcome   = $null
        }
        Save-Run $run
        Write-Output "loop-run $($run.id) initialised | workflow=$Workflow | ceiling=$MaxTurns turns"
        Write-Output "Current step: gather"
    }
    'step' {
        $run = Load-Run
        if (-not $run) { Write-Error 'No loop is running. Run -Action init first.'; exit 1 }
        if ($Step) { $run.step = $Step }
        $run.turns = [int]$run.turns + 1
        $run.history += @{ step = $run.step; at = (Get-Date).ToString('o') }
        Save-Run $run

        Write-Output "Step: $($run.step) | turn $($run.turns)/$($run.maxTurns)"
        # AG-7: do not just STOP - let the model KNOW the budget is running out so it can pace itself.
        $left = [int]$run.maxTurns - [int]$run.turns
        if ($left -le 0) {
            Write-Output 'BUDGET EXHAUSTED. Stop, summarise what got done and what is left unfinished.'
            exit 2
        }
        if ($left -le 3) {
            Write-Output "BUDGET WARNING: $left turns left. Wrap up - finishing one thing beats opening another."
        }
    }
    'status' {
        $run = Load-Run
        if (-not $run) { Write-Output 'No loop is running.'; exit 0 }
        Write-Output "id=$($run.id) workflow=$($run.workflow) step=$($run.step) turns=$($run.turns)/$($run.maxTurns)"
        Write-Output "title: $($run.title)"
        if ($run.outcome) { Write-Output "outcome: $($run.outcome)" }
    }
    'close' {
        $run = Load-Run
        if (-not $run) { Write-Output 'No loop is running.'; exit 0 }
        $run.outcome = $Outcome
        $run.step    = 'closed'
        Save-Run $run
        $archive = Join-Path $state 'runs'
        if (-not (Test-Path $archive)) { New-Item -ItemType Directory -Path $archive -Force | Out-Null }
        Copy-Item $runFile (Join-Path $archive "$($run.id).json") -Force
        Remove-Item $runFile -Force
        Write-Output "loop-run $($run.id) closed with outcome: $Outcome (saved to .claude/state/runs/$($run.id).json)"
    }
}
