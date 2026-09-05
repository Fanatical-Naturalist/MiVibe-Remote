param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNetPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $DotNetPath = Join-Path $PSScriptRoot '..\.local\dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "The project .NET SDK was not found: $DotNetPath"
}

$projectPath = Join-Path $PSScriptRoot 'Test-CodexNavigation\NavigationRegression.csproj'
& $DotNetPath run --project $projectPath --configuration $Configuration --no-launch-profile
if ($LASTEXITCODE -ne 0) {
    throw "Offline navigation regression tests failed (exit $LASTEXITCODE)."
}
