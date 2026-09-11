# PostToolUse matcher "Edit|Write", async:true -> enforce formatting 100% of the time.
# Why a hook and not a line in CLAUDE.md: instructions are PROBABILISTIC, hooks are DETERMINISTIC (HR-1).
# NOTE: PostToolUse CANNOT block a tool (it has already run). exit 2 here only hands stderr to the model.
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
            [Console]::Error.WriteLine("dotnet format reported an error on $rel :`n$($r.Output)")
            exit 1   # does not block, only logs
        }
    }
}
elseif ($ext -in @('.ts', '.tsx', '.js', '.jsx', '.json', '.css', '.md')) {
    if (Test-Path (Join-Path $root 'package.json')) {
        $null = Invoke-Quietly "npx --no-install prettier --write ""$file"""
    }
}
exit 0
