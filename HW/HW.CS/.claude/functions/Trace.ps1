<#
.SYNOPSIS
  Doc .claude/state/*.jsonl va in ba chi so van hanh cua loop.

.DESCRIPTION
  Ba chi so nay quan trong hon "do chinh xac" (AI-03, AG-12):

    1. Turns per completed task (p50/p95)  -> tang dot bien = model dang mo,
                                              nghia la TOOL hoac PROMPT hong, khong phai model kem.
    2. Cost per completed task             -> chi so kinh te duy nhat dang theo doi.
                                              KHONG phai cost per request.
    3. Tool error rate theo TUNG tool      -> tool nao hay loi la tool co mo ta/schema te.

  Template nay khong doc duoc token usage tu transcript, nen cot chi phi de trong -
  noi vao gateway cua ban (xem .claude/llms/README.md) de co so that.

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
    Write-Output 'Chua co du lieu. Hook audit-log.ps1 se sinh .claude/state/audit.jsonl khi ban lam viec.'
    exit 0
}

# --- 1. Khoi luong theo tool ---
Write-Output ''
Write-Output "-- Tool duoc goi nhieu nhat (top $Top) --"
$audit | Where-Object { $_.event -eq 'tool' } |
    Group-Object tool | Sort-Object Count -Descending | Select-Object -First $Top |
    ForEach-Object { '{0,-22} {1,5}' -f $_.Name, $_.Count } | Write-Output

# --- 2. Hanh dong bi chan ---
$blocked = @($audit | Where-Object { $_.event -eq 'blocked' })
Write-Output ''
Write-Output "-- Bi hook chan: $($blocked.Count) lan --"
$blocked | Group-Object reason | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output

# --- 3. Thrashing: tin hieu SOM nhat cho thay loop hong ---
$thrash = @($audit | Where-Object { $_.event -eq 'thrashing' })
Write-Output ''
Write-Output "-- Thrashing (lap cung tool + cung tham so): $($thrash.Count) lan --"
$thrash | Group-Object tool | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output
if ($thrash.Count -gt 0) {
    Write-Output '   => Doc lai mo ta cua nhung tool nay. Tool hay loi = tool co description/schema te (AG-8).'
}

# --- 4. Kinh te multi-agent ---
Write-Output ''
Write-Output "-- Uy thac subagent: $($subs.Count) lan --"
$subs | Group-Object agent | Sort-Object Count -Descending |
    ForEach-Object { '{0,5}  {1}' -f $_.Count, $_.Name } | Write-Output
if ($subs.Count -gt 0) {
    Write-Output '   => Moi subagent la mot context rieng phai nap lai project context (MA-2).'
    Write-Output '      Neu mot loai agent duoc goi rat nhieu cho viec NGAN, no dang lo: lam thang trong luong chinh re hon.'
}

# --- 5. Cac lan chay da dong ---
$runs = Join-Path $state 'runs'
if (Test-Path $runs) {
    $all = Get-ChildItem $runs -Filter *.json | ForEach-Object {
        try { Get-Content $_.FullName -Raw -Encoding utf8 | ConvertFrom-Json } catch { }
    }
    if ($all) {
        Write-Output ''
        Write-Output "-- Loop da dong: $($all.Count) --"
        $turns  = @($all | ForEach-Object { [int]$_.turns } | Sort-Object)
        $p50    = $turns[[int][math]::Floor($turns.Count * 0.5)]
        $p95i   = [int][math]::Floor($turns.Count * 0.95); if ($p95i -ge $turns.Count) { $p95i = $turns.Count - 1 }
        $done   = @($all | Where-Object { $_.outcome -eq 'done' }).Count
        Write-Output ("Turns per task: p50={0}  p95={1}" -f $p50, $turns[$p95i])
        Write-Output ("Ti le hoan thanh: {0}/{1}" -f $done, $all.Count)
        Write-Output ("Cham tran ngan sach: {0}" -f @($all | Where-Object { [int]$_.turns -ge [int]$_.maxTurns }).Count)
        Write-Output '   => Cham tran nhieu = tran dat sai HOAC agent dang ket. Doc trajectory, dung doc cau tra loi cuoi.'
    }
}
