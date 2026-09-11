<#
.SYNOPSIS
  The loop's verifier. "Done" is not a sentence - it is this file's exit code.

.DESCRIPTION
  The verifier trust ladder (AI-03, AG-5), from cheap to expensive:
    A. compiler / type-checker / linter   <- cheap, fast, absolutely right within its scope
    B. unit / integration test            <- strongest when the tests are trustworthy
    C. assert against real state          <- re-query the DB / call the API again and read it
    D. LLM-as-judge against a rubric      <- only when A-C are unavailable
    E. human approval                     <- most expensive, for irreversible actions

  This file implements A and B. C belongs in your repo. For D/E see .claude/skills/eval-harness.

  "Agent quality is proportional to the quality of the verifier in its environment,
   not to the length of the prompt."

.PARAMETER Level
  build | test | full   (default: full)

.PARAMETER Json
  Print the result as JSON so other hooks / commands can read it.

.OUTPUTS
  exit 0 = PASS, exit 1 = FAIL. On FAIL, print the ORIGINAL error message - no summary,
  no interpretation: the model needs to read exactly what the compiler said (AG-9).
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
        if (-not $r.passed) { $Level = 'build' }   # a red build means the tests do not run
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
        Write-Output "[$mark] $($r.stage) (tier $($r.grade) verifier, $($r.ms) ms) :: $($r.command)"
        if (-not $r.passed -and $r.errors.Count) {
            Write-Output ''
            $r.errors | ForEach-Object { Write-Output "    $_" }
            Write-Output ''
        }
    }
    Write-Output $(if ($passed) { 'VERIFY: PASS' } else { 'VERIFY: FAIL - completion may not be declared.' })
}

exit $(if ($passed) { 0 } else { 1 })
