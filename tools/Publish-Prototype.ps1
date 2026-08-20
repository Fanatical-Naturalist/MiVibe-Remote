[CmdletBinding()]
param(
    [string]$Version = "v0.1.0-prototype"
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = Join-Path $projectRoot "artifacts"
$packageName = "MiVibe-Remote-$Version-win-x64"
$publishDirectory = Join-Path $artifactRoot $packageName
$zipPath = Join-Path $artifactRoot "$packageName.zip"
$zipChecksumPath = "$zipPath.sha256"
$trayProject = Join-Path $projectRoot "src\MiVibe.Remote.Tray\MiVibe.Remote.Tray.csproj"
$releaseNotes = Join-Path $projectRoot "docs\RELEASE_V0.1.0_PROTOTYPE.md"

if ((Test-Path -LiteralPath $publishDirectory) -or
    (Test-Path -LiteralPath $zipPath) -or
    (Test-Path -LiteralPath $zipChecksumPath)) {
    throw "Artifact already exists. Refusing to overwrite frozen output: $packageName"
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

& dotnet publish $trayProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $publishDirectory `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $publishDirectory "RELEASE_NOTES.md")

$checksumPath = Join-Path $publishDirectory "SHA256SUMS.txt"
$checksumLines = Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace("\", "/")
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Compress-Archive -LiteralPath $publishDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $zipChecksumPath,
    "$zipHash  $packageName.zip`n",
    [Text.UTF8Encoding]::new($false))

Write-Output "Frozen package: $publishDirectory"
Write-Output "ZIP:            $zipPath"
Write-Output "ZIP checksum:   $zipChecksumPath"
Write-Output "ZIP SHA256:     $zipHash"
