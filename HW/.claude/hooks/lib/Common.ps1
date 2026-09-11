# .claude/hooks/lib/Common.ps1
# Shared library for every hook. Dot-source it: . "$PSScriptRoot/lib/Common.ps1"
#
# The hook I/O contract (AI-05, HR-5):
#   IN   : JSON on stdin
#   OUT  : exit 0 = ok | exit 2 = BLOCK | anything else = a non-blocking error
#          or print JSON on stdout for structured control.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-HookInput {
    <# Read and parse the JSON on stdin. Returns an empty PSCustomObject when there is nothing. #>
    try {
        $raw = [Console]::In.ReadToEnd()
        if ([string]::IsNullOrWhiteSpace($raw)) { return [pscustomobject]@{} }
        return $raw | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{}
    }
}

function Get-Prop {
    <# Safe nested property access: Get-Prop $o 'tool_input.command' #>
    param([object]$Obj, [string]$Path, $Default = $null)
    $cur = $Obj
    foreach ($seg in $Path.Split('.')) {
        if ($null -eq $cur) { return $Default }
        $p = $cur.PSObject.Properties[$seg]
        if ($null -eq $p) { return $Default }
        $cur = $p.Value
    }
    if ($null -eq $cur) { return $Default }
    return $cur
}

function Get-ProjectDir {
    if ($env:CLAUDE_PROJECT_DIR) { return $env:CLAUDE_PROJECT_DIR }
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Get-StateDir {
    $d = Join-Path (Get-ProjectDir) '.claude\state'
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    return $d
}

function Get-EnvInt {
    param([string]$Name, [int]$Default)
    $v = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($v)) { return $Default }
    $n = 0
    if ([int]::TryParse($v, [ref]$n)) { return $n }
    return $Default
}

function Test-Pattern {
    <#
      Safe regex matching. A broken pattern must NOT bring the hook down:
      this hook sits on the PreToolUse path of EVERY Bash command, so one ArgumentException
      would produce noise on every tool call and cost the user their trust in the whole harness.
      A broken pattern => return $false + log it, and the remaining patterns are still checked.
    #>
    param([string]$Text, [string]$Pattern)
    try {
        # 2-second ceiling: depth-wise defence against catastrophic backtracking.
        # A timeout counts as "no match" rather than hanging the agent's loop.
        return [regex]::IsMatch($Text, $Pattern,
                                [Text.RegularExpressions.RegexOptions]::None,
                                [TimeSpan]::FromSeconds(2))
    } catch {
        [Console]::Error.WriteLine("Hook warning: broken or too-slow regex pattern, skipped -> $Pattern")
        return $false
    }
}

function Deny-Tool {
    <# PreToolUse: block the tool and give the model a readable reason. Exit 0 because the JSON carries the decision. #>
    param([string]$Reason)
    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $Reason
        }
    } | ConvertTo-Json -Depth 6 -Compress | Write-Output
    exit 0
}

function Ask-Tool {
    param([string]$Reason)
    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'request'
            permissionDecisionReason = $Reason
        }
    } | ConvertTo-Json -Depth 6 -Compress | Write-Output
    exit 0
}

function Add-Context {
    <# Inject context into the model's context window (SessionStart / UserPromptSubmit). #>
    param([string]$Text, [string]$EventName = 'UserPromptSubmit')
    @{
        hookSpecificOutput = @{
            hookEventName     = $EventName
            additionalContext = $Text
        }
    } | ConvertTo-Json -Depth 6 -Compress | Write-Output
    exit 0
}

function Block-Stop {
    <# Stop/SubagentStop: exit 2 = do NOT allow stopping. The message is taken from stderr. #>
    param([string]$Reason)
    [Console]::Error.WriteLine($Reason)
    exit 2
}

function Write-Jsonl {
    param([string]$File, [hashtable]$Record)
    $Record['ts'] = (Get-Date).ToString('o')
    $line = ($Record | ConvertTo-Json -Depth 8 -Compress)
    $path = Join-Path (Get-StateDir) $File
    Add-Content -Path $path -Value $line -Encoding utf8
}

function Invoke-Quietly {
    <# Run a shell command, return @{ Code=int; Output=string }. Never throws. #>
    param([string]$Command, [int]$TimeoutSec = 240)
    try {
        $out = & cmd /c "$Command" 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Output = $out }
    } catch {
        return @{ Code = 1; Output = "$_" }
    }
}
