<#
.SYNOPSIS
  Verifier cua loop. "Xong" khong phai la mot cau van - la exit code cua file nay.

.DESCRIPTION
  Thang do tin cay cua verifier (AI-03, AG-5), chay tu re den dat:
    A. compiler / type-checker / linter   <- re, nhanh, tuyet doi dung trong pham vi cua no
    B. unit / integration test            <- manh nhat khi test dang tin
    C. assert tren trang thai that        <- query lai DB / goi lai API de doc
    D. LLM-as-judge theo rubric           <- chi khi khong co A-C
    E. nguoi duyet                        <- dat nhat, danh cho hanh dong khong hoan tac

  File nay cai dat A va B. C dat trong repo cua ban. D/E xem .claude/skills/eval-harness.

  "Chat luong agent ti le thuan voi chat luong verifier trong moi truong cua no,
   chu khong ti le thuan voi do dai prompt."

.PARAMETER Level
  build | test | full   (mac dinh: full)

.PARAMETER Json
  In ket qua dang JSON de hook / command khac doc.

.OUTPUTS
  exit 0 = PASS, exit 1 = FAIL. Khi FAIL, in ra thong diep loi GOC - khong tom tat,
  khong dien giai: model can doc chinh xac cai compiler noi (AG-9).
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'full')] [string]$Level = 'full',
    [switch]$Json
)

$ErrorActionPreference = 'Continue'
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }

$buildCmd = if ($env:LOOP_BUILD_CMD) { $env:LOOP_BUILD_CMD } else { 'dotnet build --nologo -v q' }
$testCmd  = if ($env:LOOP_TEST_CMD)  { $env:LOOP_TEST_CMD }  else { 'dotnet test --nologo -v q' }

$results = New-Object System.Collections.Generic.List[object]

function Run-Stage {
    param([string]$Name, [string]$Grade, [string]$Command)
    $sw  = [Diagnostics.Stopwatch]::StartNew()
    $out = & cmd /c "$Command" 2>&1 | Out-String
    $code = $LASTEXITCODE
    $sw.Stop()
    $errs = ($out -split "`r?`n" |
             Where-Object { $_ -match '(?i)\berror\b|\bfailed\b|:\s*error' } |
             Select-Object -First 30)
    return [pscustomobject]@{
        stage    = $Name
        grade    = $Grade
        command  = $Command
        passed   = ($code -eq 0)
        exitCode = $code
        ms       = [int]$sw.ElapsedMilliseconds
        errors   = @($errs)
    }
}

Push-Location $root
try {
    if ($Level -in @('build', 'full')) {
        $r = Run-Stage -Name 'build' -Grade 'A' -Command $buildCmd
        $results.Add($r)
        if (-not $r.passed) { $Level = 'build' }   # build do thi khong chay test
    }
    if ($Level -eq 'full') {
        $results.Add((Run-Stage -Name 'test' -Grade 'B' -Command $testCmd))
    }
} finally { Pop-Location }

$passed = -not ($results | Where-Object { -not $_.passed })

if ($Json) {
    [pscustomobject]@{ passed = $passed; stages = $results } | ConvertTo-Json -Depth 6
} else {
    foreach ($r in $results) {
        $mark = if ($r.passed) { 'PASS' } else { 'FAIL' }
        Write-Output "[$mark] $($r.stage) (verifier hang $($r.grade), $($r.ms) ms) :: $($r.command)"
        if (-not $r.passed -and $r.errors.Count) {
            Write-Output ''
            $r.errors | ForEach-Object { Write-Output "    $_" }
            Write-Output ''
        }
    }
    Write-Output $(if ($passed) { 'VERIFY: PASS' } else { 'VERIFY: FAIL - chua duoc tuyen bo hoan thanh.' })
}

exit $(if ($passed) { 0 } else { 1 })
