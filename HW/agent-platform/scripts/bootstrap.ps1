# Create a virtual environment, install dependencies, and verify the platform.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

python -m venv .venv
& .\.venv\Scripts\Activate.ps1

python -m pip install --upgrade pip
python -m pip install -r requirements.txt

$DotEnv = '.env'
if (-not (Test-Path $DotEnv)) {
    Copy-Item 'env.example' $DotEnv
    Write-Host "created $DotEnv from env.example -- fill in the credentials before a live run"
}

python scripts/healthcheck.py
python -m pytest -q
Write-Host ''
Write-Host "Ready.  Try:  python cli.py --dry-run run '/fixbug DEMO-1'"
