#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'

if (-not $CheckOnly) {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
    $isAdministrator = $currentPrincipal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdministrator) {
        try {
            $powerShellPath = (Get-Process -Id $PID).Path
            $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
            $elevatedProcess = Start-Process `
                -FilePath $powerShellPath `
                -Verb RunAs `
                -ArgumentList $arguments `
                -Wait `
                -PassThru
            exit $elevatedProcess.ExitCode
        }
        catch {
            Write-Error 'Administrator approval is required to disable the MiVibe key mapping.'
            exit 1223
        }
    }
}

$registryPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout'
$valueName = 'Scancode Map'

[byte[]]$splitVoiceMap = @(
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x03, 0x00, 0x00, 0x00,
    0x35, 0xE0, 0x5E, 0xE0,
    0x64, 0x00, 0x3F, 0x00,
    0x00, 0x00, 0x00, 0x00
)
[byte[]]$f5ToNumpadDivideMap = @(
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x02, 0x00, 0x00, 0x00,
    0x35, 0xE0, 0x3F, 0x00,
    0x00, 0x00, 0x00, 0x00
)
[byte[]]$legacyF13Map = @(
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x02, 0x00, 0x00, 0x00,
    0x64, 0x00, 0x3F, 0x00,
    0x00, 0x00, 0x00, 0x00
)

function ConvertTo-Hex([byte[]]$Value) {
    return ($Value | ForEach-Object { $_.ToString('X2') }) -join ''
}

$existingProperty = Get-ItemProperty -LiteralPath $registryPath -Name $valueName -ErrorAction SilentlyContinue
if ($null -eq $existingProperty) {
    Write-Host 'No Scancode Map is configured. The MiVibe voice remap is already disabled.'
    exit 0
}

[byte[]]$existingMap = $existingProperty.$valueName
$existingHex = ConvertTo-Hex $existingMap
$allowedHex = @(
    (ConvertTo-Hex $splitVoiceMap),
    (ConvertTo-Hex $f5ToNumpadDivideMap),
    (ConvertTo-Hex $legacyF13Map)
)

if ($existingHex -notin $allowedHex) {
    if ($CheckOnly) {
        Write-Host 'A non-MiVibe Scancode Map is present. It will not be changed.'
        exit 0
    }

    throw "The current Scancode Map is not a MiVibe-managed value. Nothing was removed. Existing value: $existingHex"
}

if ($CheckOnly) {
    Write-Host 'An exact MiVibe-managed Scancode Map is present.'
    exit 10
}

Remove-ItemProperty -LiteralPath $registryPath -Name $valueName
Write-Host 'Removed the MiVibe voice scan-code mapping.'
Write-Host 'Restart Windows to restore the original Power and F5 keys.'
