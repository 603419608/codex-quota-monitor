$ErrorActionPreference = "Stop"

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPaths = @(
    (Join-Path $startupDirectory "Codex Quota Monitor.lnk"),
    (Join-Path $startupDirectory "Codex Usage Overlay.lnk")
)

foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath
        Write-Host "Removed startup shortcut: $shortcutPath"
    }
    else {
        Write-Host "Startup shortcut not found: $shortcutPath"
    }
}
