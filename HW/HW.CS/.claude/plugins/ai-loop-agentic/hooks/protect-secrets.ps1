# PreToolUse matcher "Read|Grep|Glob": chan doc bi mat, ke ca khi model "co y tot".
# permissions.deny da chan Read; hook nay phu them Grep/Glob va cho model LY DO doc duoc.
. "$PSScriptRoot/lib/Common.ps1"
$in   = Read-HookInput
$path = [string](Get-Prop $in 'tool_input.file_path' '')
if (-not $path) { $path = [string](Get-Prop $in 'tool_input.path' '') }
if (-not $path) { exit 0 }

$patterns = @(
    # [/\x5C] = dau gach cheo hoac gach nguoc. Dung \x5C thay cho backslash doi
    # de mau regex khong bi hieu nham qua cac tang trich dan.
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
        Deny-Tool "Duong dan chua bi mat, bi chan boi chinh sach repo: $path`nNeu can biet cau hinh, doc TEN key trong appsettings.Development.json hoac hoi nguoi dung. Khong tim cach doc gia tri."
    }
}
exit 0
