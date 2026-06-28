$ErrorActionPreference = "Stop"

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory "Codex Usage Overlay.lnk"

if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath
    Write-Host "Removed startup shortcut: $shortcutPath"
}
else {
    Write-Host "Startup shortcut not found: $shortcutPath"
}
