# PostToolUseFailure matcher "*": phat hien THRASHING (AG-6 dieu kien dung so 4, AG-8).
#
# Cach re nhat de biet agent dang KET chu khong phai dang CO GANG:
#   bam (tool_name + tool_input) va dem. Lap y het N lan  =>  doi chien luoc hoac dung.
# Day la thu ma model KHONG tu lam duoc: no khong nho minh da thu gi o luot truoc.
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput

$tool = [string](Get-Prop $in 'tool_name' '')
# KHONG dat ten bien la $input: do la BIEN TU DONG cua PowerShell (enumerator cua pipeline).
# Ghi de no lam script hong mot cach im lang - chay xong, exit 0, khong lam gi ca.
$toolInput = Get-Prop $in 'tool_input' $null
if (-not $tool) { exit 0 }

$raw  = "$tool|" + (($toolInput | ConvertTo-Json -Depth 8 -Compress))
$md5  = [System.Security.Cryptography.MD5]::Create()
$hash = [BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($raw))).Replace('-', '').Substring(0, 12)

$file  = Join-Path (Get-StateDir) 'loop-progress.json'
$state = @{}
if (Test-Path $file) {
    try {
        (Get-Content $file -Raw -Encoding utf8 | ConvertFrom-Json).PSObject.Properties |
            ForEach-Object { $state[$_.Name] = [int]$_.Value }
    } catch { $state = @{} }
}
$count = 0
if ($state.ContainsKey($hash)) { $count = $state[$hash] }
$count++
$state[$hash] = $count
($state | ConvertTo-Json -Depth 4) | Set-Content -Path $file -Encoding utf8

$limit = Get-EnvInt 'LOOP_MAX_SAME_ERROR' 3
if ($count -ge $limit) {
    Write-Jsonl -File 'audit.jsonl' -Record @{ event = 'thrashing'; tool = $tool; count = $count }
    # exit 2 o su kien PostToolUse* = dua stderr cho model doc de no TU SUA huong.
    [Console]::Error.WriteLine(@"
DUNG LAI - PHAT HIEN THRASHING (hook .claude/hooks/loop-progress.ps1)
Ban da goi '$tool' voi CUNG THAM SO va that bai $count lan lien tiep.
Lap lai lan nua se that bai y het. BAT BUOC doi huong, chon MOT trong ba:
  1. Doi gia thuyet: doc lai thong diep loi GOC va dat mot gia thuyet KHAC ve nguyen nhan.
  2. Doi cong cu: dung tool khac, hoac thu hep pham vi (mot file, mot test).
  3. Dung va bao cao: neu khong con gia thuyet, tom tat da thu gi - that bai the nao - can gi tu nguoi dung.
KHONG duoc goi lai '$tool' voi tham so cu.
"@)
    exit 2
}
exit 0
