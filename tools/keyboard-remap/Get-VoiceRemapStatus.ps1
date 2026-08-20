[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$registryPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout'
$valueName = 'Scancode Map'
$splitVoiceHex = '00000000000000000300000035E05EE064003F0000000000'
$f5ToNumpadDivideHex = '00000000000000000200000035E03F0000000000'
$legacyF13Hex = '00000000000000000200000064003F0000000000'

$existingProperty = Get-ItemProperty -LiteralPath $registryPath -Name $valueName -ErrorAction SilentlyContinue
if ($null -eq $existingProperty) {
    Write-Host 'Status: disabled (no Scancode Map exists).'
    exit 0
}

[byte[]]$existingMap = $existingProperty.$valueName
$existingHex = ($existingMap | ForEach-Object { $_.ToString('X2') }) -join ''

switch ($existingHex) {
    $splitVoiceHex {
        Write-Host 'Status: the MiVibe split voice mapping is configured.'
        Write-Host '  Remote Power -> Numpad Divide'
        Write-Host '  F5           -> F13'
        Write-Host 'A Windows restart is required after every enable or disable operation.'
        exit 0
    }
    $f5ToNumpadDivideHex {
        Write-Warning 'Status: the earlier F5 -> Numpad Divide experiment is still configured.'
        exit 1
    }
    $legacyF13Hex {
        Write-Warning 'Status: the failed legacy F5 -> F13 experiment is still configured.'
        exit 1
    }
    default {
        Write-Warning 'Status: a different Scancode Map exists; MiVibe will not modify it automatically.'
        Write-Host "Current value: $existingHex"
        exit 2
    }
}
