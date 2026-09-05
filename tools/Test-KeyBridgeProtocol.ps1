param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^0\.3\.0-alpha\.[1-9][0-9]*$') { throw 'Invalid preview version.' }
$assemblyPath = Join-Path $projectRoot "artifacts\release\MiVibe-Remote-$Version-win-x64\MiVibe.Remote.Tray.dll"
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$method = $assembly.GetType('MiVibe.Remote.Tray.KeyBridgeMessage').GetMethod('Parse')
$valid = @(
    '{"type":"heartbeat"}',
    '{"type":"status","phase":"calibrating","step":0,"detail":"Ready"}',
    '{"type":"status","phase":"active","detail":"Ready"}',
    '{"type":"key","key":"volume_up","sequence":1}'
)
$invalid = @(
    'null', '[]', '{"type":1}', '{"type":"heartbeat","extra":true}',
    '{"type":"heartbeat","type":"heartbeat"}',
    '{"type":"status","phase":"active"}',
    '{"type":"status","phase":{},"detail":"x"}',
    '{"type":"status","phase":"calibrating","detail":"x"}',
    '{"type":"status","phase":"calibrating","step":3,"detail":"x"}',
    '{"type":"status","phase":"calibrating","step":"1","detail":"x"}',
    '{"type":"key","key":"tv","sequence":1}',
    '{"type":"key","key":"back","sequence":0}',
    '{"type":"key","key":"back","sequence":true}',
    '{"type":"key","key":"back","sequence":1.5}',
    '{"type":"key","key":"back","sequence":9223372036854775808}'
)
foreach ($line in $valid) { [void]$method.Invoke($null, @($line)) }
foreach ($line in $invalid) {
    $rejected = $false
    try { [void]$method.Invoke($null, @($line)) }
    catch { if ($_.Exception.ToString().Contains('JsonException')) { $rejected = $true } else { throw } }
    if (-not $rejected) { throw "Invalid helper message was accepted: $line" }
}
"Packaged protocol verified: $($valid.Count) valid, $($invalid.Count) malformed messages. No hardware/input initialization."
