# Stop: a DETERMINISTIC quality gate against "stopping short" (AG-8).
#
# The idea: the model does not get to declare DONE. The verifier declares it (AG-5).
# exit 2 = do NOT allow stopping; the stderr message goes back to the model so it keeps fixing.
#
# OFF BY DEFAULT (LOOP_QUALITY_GATE=0) because a build+test gate on EVERY turn is very slow.
# Turn it on for long pipelines:  $env:LOOP_QUALITY_GATE = '1'
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput

if ($env:LOOP_QUALITY_GATE -ne '1') { exit 0 }

# Avoid an infinite loop: if the previous Stop was already blocked by this very hook, let it through.
if ([bool](Get-Prop $in 'stop_hook_active' $false)) { exit 0 }

$root = Get-ProjectDir

# Only gate when source actually changed - a read-only session should not pay for a build.
# --untracked-files=all: without it, a NEW directory shows up as a single "?? directory/" line
# and every .cs file inside it is missed - the quality gate silently does not run.
$dirty = (Invoke-Quietly "git -C ""$root"" status --porcelain --untracked-files=all").Output
if (-not (Test-Pattern -Text $dirty -Pattern '(?m)\.(cs|ts|tsx|js|jsx|sql)\s*$')) { exit 0 }

$buildCmd = if ($env:LOOP_BUILD_CMD) { $env:LOOP_BUILD_CMD } else { 'dotnet build --nologo -v q' }
$build = Invoke-Quietly $buildCmd
if ($build.Code -ne 0) {
    $tail = ($build.Output -split "`r?`n" | Where-Object { $_ -match 'error|Error' } | Select-Object -First 25) -join "`n"
    Block-Stop @"
NOT DONE - BUILD IS RED (Stop hook, .claude/hooks/stop-quality-gate.ps1)
Command: $buildCmd
$tail

Fix every build error before ending the turn. If the errors are unrelated to your changes,
say so explicitly and show the evidence (file:line, commit) before stopping.
"@
}

$testCmd = if ($env:LOOP_TEST_CMD) { $env:LOOP_TEST_CMD } else { '' }
if ($testCmd) {
    $test = Invoke-Quietly $testCmd
    if ($test.Code -ne 0) {
        $tail = ($test.Output -split "`r?`n" | Where-Object { $_ -match 'Failed|error' } | Select-Object -First 25) -join "`n"
        Block-Stop @"
NOT DONE - TESTS ARE RED (Stop hook)
Command: $testCmd
$tail

Keep fixing until the tests are green, or explain why the red test is expected and what the user needs to know.
"@
    }
}
exit 0
