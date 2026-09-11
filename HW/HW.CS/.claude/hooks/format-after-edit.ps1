# PostToolUse matcher "Edit|Write", async:true -> ep chuan format 100% lan.
# Vi sao la hook chu khong phai mot dong trong CLAUDE.md: chi dan la XAC SUAT, hook la TAT DINH (HR-1).
# LUU Y: PostToolUse KHONG ngan duoc tool (no da chay roi). exit 2 o day chi dua stderr cho model doc.
. "$PSScriptRoot/lib/Common.ps1"
$in   = Read-HookInput
$file = [string](Get-Prop $in 'tool_input.file_path' '')
if (-not $file -or -not (Test-Path $file)) { exit 0 }

$root = Get-ProjectDir
$ext  = [IO.Path]::GetExtension($file).ToLowerInvariant()

function Find-OwningProject {
    param([string]$FromFile, [string]$Root)
    $dir = Split-Path $FromFile -Parent
    while ($dir -and $dir.Length -ge $Root.Length) {
        $p = Get-ChildItem -Path $dir -Filter *.csproj -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($p) { return $p }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}

if ($ext -eq '.cs') {
    $proj = Find-OwningProject -FromFile $file -Root $root
    if ($proj) {
        $rel = [IO.Path]::GetRelativePath($proj.DirectoryName, $file)
        $r = Invoke-Quietly "dotnet format ""$($proj.FullName)"" --include ""$rel"" --verbosity quiet --no-restore"
        if ($r.Code -ne 0) {
            [Console]::Error.WriteLine("dotnet format bao loi tren $rel :`n$($r.Output)")
            exit 1   # khong chan, chi ghi log
        }
    }
}
elseif ($ext -in @('.ts', '.tsx', '.js', '.jsx', '.json', '.css', '.md')) {
    if (Test-Path (Join-Path $root 'package.json')) {
        $null = Invoke-Quietly "npx --no-install prettier --write ""$file"""
    }
}
exit 0
