param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$pluginRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $pluginRoot "src\CodexUsageOverlay.App\CodexUsageOverlay.App.csproj"

if (-not $NoBuild) {
    dotnet build $appProject
}

dotnet run --project $appProject --no-build
