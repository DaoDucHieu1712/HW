# .claude/hooks/lib/Common.ps1
# Thu vien dung chung cho moi hook. Dot-source: . "$PSScriptRoot/lib/Common.ps1"
#
# Hop dong I/O cua hook (AI-05, HR-5):
#   VAO  : JSON qua stdin
#   RA   : exit 0 = ok | exit 2 = CHAN | khac = loi khong chan
#          hoac in JSON ra stdout de dieu khien co cau truc.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-HookInput {
    <# Doc va parse JSON tu stdin. Tra ve PSCustomObject rong neu khong co gi. #>
    try {
        $raw = [Console]::In.ReadToEnd()
        if ([string]::IsNullOrWhiteSpace($raw)) { return [pscustomobject]@{} }
        return $raw | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{}
    }
}

function Get-Prop {
    <# Truy cap thuoc tinh long nhau an toan: Get-Prop $o 'tool_input.command' #>
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
      So khop regex an toan. Mot mau hong KHONG duoc lam sap hook:
      hook nay nam tren duong PreToolUse cua MOI lenh Bash, nen mot ArgumentException
      se sinh nhieu o moi lan goi tool va lam nguoi dung mat long tin vao ca harness.
      Mau hong => tra $false + ghi log, cac mau con lai van duoc kiem tra.
    #>
    param([string]$Text, [string]$Pattern)
    try {
        # Tran 2 giay: phong ve theo chieu sau chong catastrophic backtracking.
        # Het gio => coi nhu khong khop, chu KHONG treo vong lap cua agent.
        return [regex]::IsMatch($Text, $Pattern,
                                [Text.RegularExpressions.RegexOptions]::None,
                                [TimeSpan]::FromSeconds(2))
    } catch {
        [Console]::Error.WriteLine("Hook canh bao: mau regex hong hoac qua cham, bi bo qua -> $Pattern")
        return $false
    }
}

function Deny-Tool {
    <# PreToolUse: chan tool, dua ly do cho model doc. Exit 0 vi JSON da mang quyet dinh. #>
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
    <# Chen ngu canh vao context cua model (SessionStart / UserPromptSubmit). #>
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
    <# Stop/SubagentStop: exit 2 = KHONG cho dung. Thong diep lay tu stderr. #>
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
    <# Chay lenh shell, tra ve @{ Code=int; Output=string }. Khong bao gio nem. #>
    param([string]$Command, [int]$TimeoutSec = 240)
    try {
        $out = & cmd /c "$Command" 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Output = $out }
    } catch {
        return @{ Code = 1; Output = "$_" }
    }
}
