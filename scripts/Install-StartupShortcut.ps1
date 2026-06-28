param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$pluginRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $pluginRoot "src\CodexUsageOverlay.App\CodexUsageOverlay.App.csproj"
$appExe = Join-Path $pluginRoot "src\CodexUsageOverlay.App\bin\Debug\net10.0-windows\CodexUsageOverlay.App.exe"

if ($Build -or -not (Test-Path $appExe)) {
    dotnet build $appProject
}

if (-not (Test-Path $appExe)) {
    throw "Codex Usage Overlay executable was not found: $appExe"
}

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory "Codex Usage Overlay.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appExe
$shortcut.WorkingDirectory = Split-Path -Parent $appExe
$shortcut.Description = "Start Codex Usage Overlay after Windows sign-in."
$shortcut.WindowStyle = 7
$shortcut.Save()

Write-Host "Installed startup shortcut: $shortcutPath"
