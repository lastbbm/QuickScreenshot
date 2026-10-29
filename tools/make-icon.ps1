$ErrorActionPreference = 'Stop'
$python = Get-Command python -ErrorAction SilentlyContinue
$script = Join-Path $PSScriptRoot 'make-icon.py'
if (-not $python) {
    $python = Get-Command py -ErrorAction SilentlyContinue
}
if (-not $python) {
    throw 'Python 3 with Pillow is required to rebuild icons. Install Pillow and run again.'
}

if ([System.IO.Path]::GetFileName($python.Source) -ieq 'py.exe') {
    & $python.Source -3 $script
} else {
    & $python.Source $script
}

if ($LASTEXITCODE -ne 0) {
    throw "Icon generation failed with exit code $LASTEXITCODE"
}
