[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$KeyBridgeDirectory = "",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^0\.3\.0-alpha\.[1-9][0-9]*$') {
    throw 'This release configuration targets 0.3.0-alpha previews.'
}
$releaseNotes = Join-Path $projectRoot "docs\RELEASE_NOTES_$Version.md"
if (-not (Test-Path -LiteralPath $releaseNotes)) {
    throw "Release notes are required for $Version."
}
if ([string]::IsNullOrWhiteSpace($KeyBridgeDirectory)) {
    $KeyBridgeDirectory = Join-Path $projectRoot 'artifacts\keybridge\MiVibe.Remote.KeyBridge'
}
if (-not (Test-Path -LiteralPath (Join-Path $KeyBridgeDirectory 'MiVibe.Remote.KeyBridge.exe'))) {
    throw 'Build the enhanced key helper first with tools/Build-KeyBridge.ps1.'
}
$artifactRoot = Join-Path $projectRoot 'artifacts\release'
$buildArtifactRoot = Join-Path $projectRoot "artifacts\build\$Version"
$packageName = "MiVibe-Remote-$Version-win-x64"
$publishDirectory = Join-Path $artifactRoot $packageName
$zipPath = Join-Path $artifactRoot "$packageName.zip"
$setupPath = Join-Path $artifactRoot "MiVibe-Remote-Setup-$Version-win-x64.exe"
$releaseChecksumsPath = Join-Path $artifactRoot "MiVibe-Remote-$Version-SHA256SUMS.txt"
$trayProject = Join-Path $projectRoot 'src\MiVibe.Remote.Tray\MiVibe.Remote.Tray.csproj'
$installerScript = Join-Path $projectRoot 'installer\MiVibe.Remote.iss'

$localDotnet = Join-Path $projectRoot '.local\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

foreach ($path in @($publishDirectory, $zipPath, $setupPath, $releaseChecksumsPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Artifact already exists. Refusing to overwrite: $path"
    }
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& $dotnet publish $trayProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --artifacts-path (Join-Path $buildArtifactRoot "tray") `
    --output $publishDirectory `
    -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=zh-Hans `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) {
    throw "Tray and bridge publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "README.zh-CN.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "LICENSE") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "THIRD-PARTY-NOTICES.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\\QUICK_START.md") -Destination $publishDirectory
Copy-Item -LiteralPath $releaseNotes `
    -Destination (Join-Path $publishDirectory "RELEASE_NOTES.md")
Copy-Item -LiteralPath $KeyBridgeDirectory -Destination (Join-Path $publishDirectory 'KeyBridge') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\assets') `
    -Destination (Join-Path $publishDirectory 'docs\assets') `
    -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot "tools\\keyboard-remap") `
    -Destination (Join-Path $publishDirectory "tools\\keyboard-remap") `
    -Recurse

$payloadChecksumPath = Join-Path $publishDirectory "SHA256SUMS.txt"
$payloadChecksumLines = Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Where-Object FullName -ne $payloadChecksumPath |
    Sort-Object FullName |
    ForEach-Object {
        # Windows PowerShell 5.1 runs on .NET Framework, which does not expose
        # Path.GetRelativePath. Every file enumerated here is already below the
        # publish directory, so a checked prefix trim is sufficient and keeps
        # the release script usable on stock Windows 11.
        if (-not $_.FullName.StartsWith(
                $publishDirectory,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Payload file escaped the publish directory: $($_.FullName)"
        }
        $relativePath = $_.FullName.Substring($publishDirectory.Length).
            TrimStart([char[]]'\/').Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }
[IO.File]::WriteAllLines(
    $payloadChecksumPath,
    $payloadChecksumLines,
    [Text.UTF8Encoding]::new($false))

Compress-Archive `
    -Path (Join-Path $publishDirectory "*") `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $innoCandidates = @(
        (Join-Path $projectRoot '.local\tools\InnoSetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $InnoCompiler = $innoCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
        Select-Object -First 1
}

if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw "Inno Setup compiler not found. Pass -InnoCompiler with the full ISCC.exe path."
}

& $InnoCompiler `
    "/DMyAppVersion=$Version" `
    "/DSourceRoot=$publishDirectory" `
    "/DOutputDir=$artifactRoot" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Expected installer was not produced: $setupPath"
}

$releaseChecksumLines = @($setupPath, $zipPath) | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines(
    $releaseChecksumsPath,
    $releaseChecksumLines,
    [Text.UTF8Encoding]::new($false))

Write-Output "Installer: $setupPath"
Write-Output "Portable:  $zipPath"
Write-Output "Checksums: $releaseChecksumsPath"
