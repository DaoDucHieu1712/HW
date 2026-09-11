# PreToolUse matcher "Read|Grep|Glob": block reading secrets, even when the model means well.
# permissions.deny already blocks Read; this hook additionally covers Grep/Glob and gives the model a readable REASON.
. "$PSScriptRoot/lib/Common.ps1"
$in   = Read-HookInput
$path = [string](Get-Prop $in 'tool_input.file_path' '')
if (-not $path) { $path = [string](Get-Prop $in 'tool_input.path' '') }
if (-not $path) { exit 0 }

$patterns = @(
    # [/\x5C] = a forward slash or a backslash. \x5C is used instead of a doubled
    # backslash so the pattern is not mangled as it passes through quoting layers.
    '(?i)[/\x5C]\.env(\.|$)',
    '(?i)[/\x5C]secrets?[/\x5C]',
    '(?i)appsettings\.Production\.json$',
    '(?i)\.(pfx|p12|pem|key)$',
    '(?i)[/\x5C]id_rsa',
    '(?i)\.aws[/\x5C]credentials',
    '(?i)\.npmrc$'
)
foreach ($p in $patterns) {
    if (Test-Pattern -Text $path -Pattern $p) {
        Write-Jsonl -File 'audit.jsonl' -Record @{ event = 'blocked'; tool = 'Read'; summary = $path; reason = 'secret' }
        Deny-Tool "This path holds secrets and is blocked by repo policy: $path`nIf you need to know the configuration, read the key NAMES in appsettings.Development.json or ask the user. Do not try to read the values."
    }
}
exit 0
