# Stop: cong chat luong TAT DINH chong "stopping short" (AG-8).
#
# Y tuong: model khong duoc quyen tuyen bo XONG. Verifier tuyen bo (AG-5).
# exit 2 = KHONG cho dung; thong diep stderr quay lai cho model de no sua tiep.
#
# MAC DINH TAT (LOOP_QUALITY_GATE=0) vi mot cong build+test tren MOI luot rat cham.
# Bat khi chay pipeline dai:  $env:LOOP_QUALITY_GATE = '1'
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput

if ($env:LOOP_QUALITY_GATE -ne '1') { exit 0 }

# Chong lap vo tan: neu lan Stop truoc da bi chan boi chinh hook nay thi cho di.
if ([bool](Get-Prop $in 'stop_hook_active' $false)) { exit 0 }

$root = Get-ProjectDir

# Chi gac khi that su co thay doi ma nguon - phien chi doc thi khong ton thoi gian build.
# --untracked-files=all: khong co no, mot thu muc MOI chi hien thanh mot dong "?? thu-muc/"
# va moi file .cs ben trong se bi bo qua - cong chat luong im lang khong chay.
$dirty = (Invoke-Quietly "git -C ""$root"" status --porcelain --untracked-files=all").Output
if (-not (Test-Pattern -Text $dirty -Pattern '(?m)\.(cs|ts|tsx|js|jsx|sql)\s*$')) { exit 0 }

$buildCmd = if ($env:LOOP_BUILD_CMD) { $env:LOOP_BUILD_CMD } else { 'dotnet build --nologo -v q' }
$build = Invoke-Quietly $buildCmd
if ($build.Code -ne 0) {
    $tail = ($build.Output -split "`r?`n" | Where-Object { $_ -match 'error|Error' } | Select-Object -First 25) -join "`n"
    Block-Stop @"
CHUA XONG - BUILD DO (hook Stop, .claude/hooks/stop-quality-gate.ps1)
Lenh: $buildCmd
$tail

Sua het loi build roi moi ket thuc luot. Neu loi khong lien quan den thay doi cua ban,
hay noi ro dieu do va chi ra bang chung (file:line, commit) truoc khi dung.
"@
}

$testCmd = if ($env:LOOP_TEST_CMD) { $env:LOOP_TEST_CMD } else { '' }
if ($testCmd) {
    $test = Invoke-Quietly $testCmd
    if ($test.Code -ne 0) {
        $tail = ($test.Output -split "`r?`n" | Where-Object { $_ -match 'Failed|error' } | Select-Object -First 25) -join "`n"
        Block-Stop @"
CHUA XONG - TEST DO (hook Stop)
Lenh: $testCmd
$tail

Sua cho den khi test xanh, hoac giai thich vi sao test do la ky vong va nguoi dung can biet gi.
"@
    }
}
exit 0
