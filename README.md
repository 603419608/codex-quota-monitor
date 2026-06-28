# Codex Usage Overlay

Windows WPF companion overlay for Codex context usage and Codex rate limits.

This plugin does not inject UI into Codex App. Codex plugins do not currently expose a native API for adding controls under the input box, so the visible part is a small local companion window.

## Features

- Shows three vertical circular gauges:
  - Current Codex conversation context remaining.
  - 5-hour usage limit remaining.
  - Weekly usage limit remaining.
- Reads context usage only from the current Codex session's bottom context indicator using UI Automation text. It does not read tooltips or chat content.
- Reads 5-hour and weekly limits from Codex app-server rate limit data.
- Follows the Codex window using a foreground WinEvent hook plus low-frequency fallback polling, so the overlay appears and hides with Codex.
- Supports Chinese, English, and French based on the Windows UI language.
- Supports manual collapse/show, hiding to the Windows system tray, dragging, saved window position, and Windows sign-in startup scripts.

## Run

From this plugin directory:

```powershell
.\scripts\Start-CodexUsageOverlay.ps1
```

To start without rebuilding first:

```powershell
.\scripts\Start-CodexUsageOverlay.ps1 -NoBuild
```

The overlay starts `codex app-server --listen stdio://` for rate limit data. To attach to an existing websocket app-server endpoint, set `CODEX_USAGE_OVERLAY_APP_SERVER_WS` before launching the overlay.

## Windows Sign-In Startup

Install the startup shortcut:

```powershell
.\scripts\Install-StartupShortcut.ps1 -Build
```

Remove the startup shortcut:

```powershell
.\scripts\Uninstall-StartupShortcut.ps1
```

## Platform Support

This companion app is Windows-only:

- It targets `net10.0-windows`.
- It uses WPF.
- It uses Windows UI Automation to read Codex App context UI.
- It uses `user32.dll` APIs for window detection.

macOS is not supported by this build. A Mac version would need a separate UI and window-reading implementation, such as SwiftUI/AppKit with macOS Accessibility APIs, or a cross-platform rewrite with a native macOS integration layer.

## Privacy & Network Behavior

This companion reads local Codex data and makes one low-frequency network call. Specifically:

- **Reads your local access token.** To show how many rate-limit reset credits you have, it reads the `access_token` from `~/.codex/auth.json` (the same credential file the Codex CLI maintains). The token stays in memory only; it is never written to disk and never logged.
- **One endpoint, at most once per day.** The token is used solely as a `Bearer` credential for a request to `https://chatgpt.com/backend-api/wham/rate-limit-reset-credits`. This request is made at most once per calendar day (and again only if the app is restarted before that day's fetch has succeeded).
- **No content is uploaded.** It does not upload chat content, prompts, source code, files, screenshots, or OCR text. It performs no screen capture, OCR, keyboard/mouse automation, or `/status` injection. Context usage is read passively from the Codex window's on-screen text via Windows UI Automation; rate limits are read locally from `codex app-server` over stdio.
- **Unofficial endpoint.** The reset-credit endpoint is an undocumented backend API. It may change or stop working at any time without notice; when it fails, the overlay keeps running and simply hides the reset-credit line.
- **Local cache.** Reset-credit results are cached at `%APPDATA%\CodexUsageOverlay\reset-credits-cache.json`. The cache stores only non-sensitive scheduling metadata (number of reset credits and their expiry/grant times). It does **not** store your token or any chat content. Window position and collapse state are stored separately in `%APPDATA%\CodexUsageOverlay\settings.json`.

## Share

To share the plugin with another Windows user:

1. Share this plugin folder without `bin/`, `obj/`, `artifacts/`, `.vs/`, or `.claude/` build, IDE, and local agent outputs.
2. Ask them to install a compatible .NET SDK/runtime and Codex.
3. Ask them to run `.\scripts\Start-CodexUsageOverlay.ps1`.
4. Optionally ask them to run `.\scripts\Install-StartupShortcut.ps1 -Build` for Windows sign-in startup.

If the tray icon is hidden under the Windows `^` menu, pin it from Windows Settings:
Personalization -> Taskbar -> Other system tray icons.

## Notes

- The companion initializes app-server with `clientInfo.name = "codex_usage_overlay"` and `experimentalApi = true`.
- It calls `account/rateLimits/read` and listens for `account/rateLimits/updated`.
- If Codex rate limit fields are unavailable, the overlay keeps running and shows unavailable gauges.
