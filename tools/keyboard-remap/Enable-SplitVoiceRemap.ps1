#Requires -RunAsAdministrator

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$registryPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout'
$valueName = 'Scancode Map'

# Two mappings plus the terminator:
#   Remote Power: E0 5E -> Numpad Divide: E0 35
#   F5:           00 3F -> F13:           00 64
#
# The second entry applies globally, so the computer keyboard F5 also becomes
# F13. F13 is used only as a quiet sink and was confirmed to be ignored by the
# current Typeless shortcut recorder.
[byte[]]$targetMap = @(
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x03, 0x00, 0x00, 0x00,
    0x35, 0xE0, 0x5E, 0xE0,
    0x64, 0x00, 0x3F, 0x00,
    0x00, 0x00, 0x00, 0x00
)

# Exact MiVibe experiment values that this migration may replace.
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

function Test-ByteArrayEqual {
    param([byte[]]$Left, [byte[]]$Right)

    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }

    return $true
}

$existingProperty = Get-ItemProperty -LiteralPath $registryPath -Name $valueName -ErrorAction SilentlyContinue
$existingMap = if ($null -eq $existingProperty) { $null } else { [byte[]]$existingProperty.$valueName }

if (Test-ByteArrayEqual -Left $existingMap -Right $targetMap) {
    Write-Host 'The MiVibe split voice mapping is already configured.'
    Write-Host 'Restart Windows if it is not active yet.'
    exit 0
}

$replaceable = $null -eq $existingMap -or
    (Test-ByteArrayEqual -Left $existingMap -Right $f5ToNumpadDivideMap) -or
    (Test-ByteArrayEqual -Left $existingMap -Right $legacyF13Map)

if (-not $replaceable) {
    $existingHex = ($existingMap | ForEach-Object { $_.ToString('X2') }) -join ''
    throw "A different Scancode Map already exists. Nothing was changed. Existing value: $existingHex"
}

if ($null -eq $existingMap) {
    New-ItemProperty -LiteralPath $registryPath -Name $valueName -PropertyType Binary -Value $targetMap | Out-Null
} else {
    Set-ItemProperty -LiteralPath $registryPath -Name $valueName -Value $targetMap
}

$writtenMap = [byte[]](Get-ItemPropertyValue -LiteralPath $registryPath -Name $valueName)
if (-not (Test-ByteArrayEqual -Left $writtenMap -Right $targetMap)) {
    throw 'The registry value did not pass read-back verification.'
}

Write-Host 'Configured the MiVibe split voice scan-code mapping:'
Write-Host '  Remote Power (E0 5E) -> Numpad Divide (Typeless toggle)'
Write-Host '  F5 (00 3F)           -> F13 (quiet microphone-key sink)'
Write-Host 'Restart Windows to activate it.'
Write-Host 'Important: the physical F5 key on every keyboard will also become F13.'
Write-Host 'Run Disable-VoiceRemap.ps1 as administrator, then restart, to restore the original keys.'
