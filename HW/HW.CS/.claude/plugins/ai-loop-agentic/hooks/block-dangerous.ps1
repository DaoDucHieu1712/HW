# PreToolUse matcher "Bash": ranh gioi TAT DINH cua repo (AI-05 HR-7).
# Hook chay du model "quen", va KHONG be duoc bang prompt injection - vi no khong doc prompt.
# Nguyen tac an toan (HR-8): luon trich truong bang parser JSON, khong bao gio Invoke-Expression.
. "$PSScriptRoot/lib/Common.ps1"
$in  = Read-HookInput
$cmd = [string](Get-Prop $in 'tool_input.command' '')
if (-not $cmd) { exit 0 }

# --- DENY: pha huy / khong hoan tac / ro ri bi mat ---
$deny = @(
    @{ Rx = '(?i)rm\s+-rf\s+[/\x5C]\s*$';                 Why = 'Xoa de quy tu thu muc goc.' },
    @{ Rx = '(?i)rm\s+-rf\s+[~$]';                         Why = 'Xoa de quy home / bien moi truong.' },
    @{ Rx = '(?i)\bgit\s+push\b.*--force(?!-with-lease)';  Why = 'Force push ghi de lich su remote. Dung --force-with-lease.' },
    @{ Rx = '(?i)\bgit\s+push\b.*\s-f(\s|$)';              Why = 'Force push ghi de lich su remote.' },
    @{ Rx = '(?i)\bgit\s+reset\s+--hard\b';                Why = 'Vut bo thay doi chua commit. Commit hoac stash truoc.' },
    @{ Rx = '(?i)\bgit\s+clean\s+-[a-z]*f';                Why = 'Xoa file chua theo doi, khong hoan tac.' },
    @{ Rx = '(?i)\bDROP\s+(TABLE|DATABASE|SCHEMA)\b';      Why = 'DDL pha huy du lieu.' },
    @{ Rx = '(?i)\bTRUNCATE\s+TABLE\b';                    Why = 'Xoa toan bo du lieu bang.' },
    # [\s\S] la MOT lop ky tu, khong phai luan phien (.|\n).
    # (.|\n)* trong lookahead gay catastrophic backtracking: hook nam tren duong
    # PreToolUse cua moi lenh Bash, treo o day la treo ca phien.
    @{ Rx = '(?i)\bDELETE\s+FROM\b(?![\s\S]*\bWHERE\b)';   Why = 'DELETE khong co WHERE.' },
    @{ Rx = '(?i)\bUPDATE\b[\s\S]+\bSET\b(?![\s\S]*\bWHERE\b)'; Why = 'UPDATE khong co WHERE.' },
    @{ Rx = '(?i)\b(cat|type|Get-Content)\b[^|]*\.env(\s|$)';   Why = 'Doc file secret.' },
    @{ Rx = '(?i)\bnpm\s+publish\b';                       Why = 'Phat hanh goi ra ngoai phai do nguoi thuc hien.' },
    @{ Rx = '(?i)\bdotnet\s+nuget\s+push\b';               Why = 'Phat hanh goi ra ngoai phai do nguoi thuc hien.' }
)
foreach ($r in $deny) {
    if (Test-Pattern -Text $cmd -Pattern $r.Rx) {
        Write-Jsonl -File 'audit.jsonl' -Record @{ event = 'blocked'; tool = 'Bash'; summary = $cmd; reason = $r.Why }
        Deny-Tool "Chan boi chinh sach repo (.claude/hooks/block-dangerous.ps1): $($r.Why) Neu that su can, nguoi dung phai tu chay lenh nay."
    }
}

# --- ASK: hop le nhung kho hoan tac (AG-11) ---
$ask = @(
    @{ Rx = '(?i)\bdotnet\s+ef\s+database\s+update\b';       Why = 'Migration doi schema that.' },
    @{ Rx = '(?i)\b(kubectl|helm)\s+(apply|delete|rollout)\b'; Why = 'Thay doi cum Kubernetes.' },
    @{ Rx = '(?i)\bdocker\s+(push|system\s+prune)\b';        Why = 'Thao tac Docker co he qua ra ngoai.' }
)
foreach ($r in $ask) {
    if (Test-Pattern -Text $cmd -Pattern $r.Rx) { Ask-Tool "Hanh dong kho hoan tac: $($r.Why) Can xac nhan cua nguoi dung." }
}

exit 0   # khong quyet dinh -> luong permission binh thuong tiep tuc
