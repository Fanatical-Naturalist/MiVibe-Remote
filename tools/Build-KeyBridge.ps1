[CmdletBinding()]
param(
    [string]$Python = "python",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = Join-Path $projectRoot 'src\MiVibe.Remote.KeyBridge'
$dependencies = Join-Path $projectRoot '.local\hid-probe-deps'
$buildRoot = Join-Path $projectRoot 'artifacts\build\keybridge'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\keybridge'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'MiVibe.Remote.KeyBridge')) {
    throw "KeyBridge output already exists: $OutputDirectory"
}
$metadata = Join-Path $dependencies 'frida-17.15.3.dist-info\METADATA'
if (-not (Test-Path -LiteralPath $metadata)) {
    throw 'Expected local Frida 17.15.3 dependency. See src/MiVibe.Remote.KeyBridge/README.md.'
}
$pyInstallerVersion = & $Python -m PyInstaller --version
if ($LASTEXITCODE -ne 0 -or "$pyInstallerVersion".Trim() -ne '6.14.2') {
    throw 'This preview is built with PyInstaller 6.14.2.'
}
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$previousPythonPath = $env:PYTHONPATH
try {
$env:PYTHONPATH = $dependencies
& $Python -m PyInstaller --noconfirm --onedir --windowed `
    --name MiVibe.Remote.KeyBridge `
    --distpath $OutputDirectory --workpath (Join-Path $buildRoot 'work') `
    --specpath $buildRoot --paths $dependencies `
    --add-data "$sourceRoot\hid_tap.js;." `
    --copy-metadata frida --collect-all frida `
    (Join-Path $sourceRoot 'main.py')
if ($LASTEXITCODE -ne 0) {
    throw "KeyBridge build failed: $LASTEXITCODE"
}
}
finally {
    $env:PYTHONPATH = $previousPythonPath
}
$payload = Join-Path $OutputDirectory 'MiVibe.Remote.KeyBridge'
$licenses = Join-Path $payload 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dependencies 'frida-17.15.3.dist-info\licenses\COPYING') `
    -Destination (Join-Path $licenses 'Frida-COPYING.txt')
$pythonLicense = & $Python -c 'import sys; from pathlib import Path; print(Path(sys.base_prefix) / "LICENSE.txt")'
$pyInstallerLicense = & $Python -c 'import importlib.metadata as m; print(m.distribution("pyinstaller").locate_file("pyinstaller-6.14.2.dist-info/licenses/COPYING.txt"))'
if (-not (Test-Path -LiteralPath $pyInstallerLicense)) {
    $pyInstallerLicense = & $Python -c 'import importlib.metadata as m; d=m.distribution("pyinstaller"); print(next(str(d.locate_file(p)) for p in d.files if str(p).endswith("COPYING.txt")))'
}
Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $licenses 'Python-LICENSE.txt')
Copy-Item -LiteralPath $pyInstallerLicense -Destination (Join-Path $licenses 'PyInstaller-COPYING.txt')
Write-Output "KeyBridge payload: $payload"
