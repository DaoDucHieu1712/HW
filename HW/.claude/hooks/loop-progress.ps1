# PostToolUseFailure matcher "*": detects THRASHING (AG-6 stop condition #4, AG-8).
#
# The cheapest way to tell that an agent is STUCK rather than TRYING:
#   hash (tool_name + tool_input) and count. Identical repeats N times  =>  change strategy or stop.
# This is something the model CANNOT do for itself: it does not remember what it tried last turn.
. "$PSScriptRoot/lib/Common.ps1"
$in = Read-HookInput

$tool = [string](Get-Prop $in 'tool_name' '')
# Do NOT name a variable $input: that is a PowerShell AUTOMATIC variable (the pipeline enumerator).
# Overwriting it breaks the script silently - it runs, exits 0, and does nothing at all.
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
    # exit 2 on a PostToolUse* event = hand stderr to the model so it can CORRECT COURSE itself.
    [Console]::Error.WriteLine(@"
STOP - THRASHING DETECTED (hook .claude/hooks/loop-progress.ps1)
You have called '$tool' with THE SAME ARGUMENTS and failed $count times in a row.
One more identical attempt will fail identically. You MUST change direction; pick ONE of three:
  1. Change hypothesis: re-read the ORIGINAL error message and form a DIFFERENT hypothesis about the cause.
  2. Change tool: use a different tool, or narrow the scope (one file, one test).
  3. Stop and report: with no hypothesis left, summarise what was tried - how it failed - what you need from the user.
Do NOT call '$tool' again with the old arguments.
"@)
    exit 2
}
exit 0
