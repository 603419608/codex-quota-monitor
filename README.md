# Codex Quota Monitor / Codex 额度监控

Windows WPF companion overlay for ChatGPT Codex context usage and rate limits.

![Preview](images/preview.png)

> ⚠️ The preview image shows an earlier UI. The current build uses an expanded circular-gauge view and a horizontal compact bar; the 5-hour gauge appears only when Codex app-server reports that window.
> ⚠️ 上图为早期界面效果。当前版本使用圆环展开模式和横向迷你条；只有 Codex app-server 返回 5 小时窗口时，才会显示对应额度。

This plugin does not inject UI into the ChatGPT desktop app. Codex plugins do not currently expose a native API for adding controls under the input box, so the visible part is a small local companion window.

---

## English

Codex Quota Monitor is an **unofficial** Windows companion overlay for the ChatGPT desktop app's Codex experience. It keeps the most useful usage signals visible without opening settings or switching context.

### Features

- Up to three vertical circular gauges:
  - Current ChatGPT Codex conversation context remaining.
  - 5-hour usage limit remaining, when that window is available.
  - Weekly usage limit remaining.
- Reset times for available rate-limit windows in expanded mode, plus available reset credits.
- The signed-in account name (email fallback) and lifetime token total in expanded mode.
- A horizontal compact bar showing context and weekly percentages, plus the 5-hour percentage when available.
- A system-tray dot whose color reflects the 5-hour remaining percentage when available, otherwise the weekly remaining percentage.
- Color states: green = 60-100% left, yellow = 20-60%, red = under 20%, gray = data unavailable.
- Reads context usage only from the current ChatGPT Codex session's bottom context indicator using UI Automation text. It does not read tooltips or chat content.
- Reads and classifies available rate-limit windows from Codex app-server data by their reported duration.
- Follows the ChatGPT window using a foreground WinEvent hook plus low-frequency fallback polling, so the overlay appears and hides with ChatGPT.
- Supports Chinese, English, and French based on the Windows UI language.
- Supports manual collapse/show, hiding to the Windows system tray, dragging, double-click-to-tray, saved window position, and Windows sign-in startup scripts.

### Important Notes

- This is not an official OpenAI product.
- This project is not affiliated with, endorsed by, or supported by OpenAI.
- Windows only. macOS is not supported because this tool uses WPF, Win32 window hooks, and Windows UI Automation.

### Install (prebuilt binary)

1. Download `codex-quota-monitor-win-x64-v0.1.9.zip` from the [latest Release](../../releases/latest).
2. Extract the zip to any folder, for example `C:\Tools\CodexQuotaMonitor`.
3. Run `CodexUsageOverlay.App.exe`.
4. Open ChatGPT and enter Codex. The overlay should appear automatically.

The package is self-contained, so you do not need to install the .NET Desktop Runtime separately.

### Run from source

Requires the .NET 10 SDK. From this folder:

```powershell
.\scripts\Start-CodexUsageOverlay.ps1
```

To start without rebuilding first:

```powershell
.\scripts\Start-CodexUsageOverlay.ps1 -NoBuild
```

The overlay starts `codex app-server --listen stdio://` for rate limit data. To attach to an existing websocket app-server endpoint, set `CODEX_USAGE_OVERLAY_APP_SERVER_WS` before launching the overlay.

### Windows Sign-In Startup

```powershell
.\scripts\Install-StartupShortcut.ps1 -Build   # install
.\scripts\Uninstall-StartupShortcut.ps1        # remove
```

### Usage

- Drag the overlay to move it.
- Click the minus button to collapse it to the compact bar; click `□` to expand again.
- Hover over the compact bar to see quota reset times.
- Double-click the overlay (or click the dot button) to hide it to the system tray; click the tray icon to restore.
- Close the overlay with the `x` button.

The overlay remembers its position and collapsed/expanded state.

### Requirements

- Windows 10/11 x64
- ChatGPT desktop app installed with Codex access
- You must be signed in to ChatGPT

### How It Works

- Context remaining is read from the current ChatGPT Codex UI indicator via Windows UI Automation.
- Available quota windows are read from Codex app-server rate limit data over local stdio. The 5-hour display hides automatically when that window is not reported and returns if the window becomes available again.
- The overlay follows the Codex window and hides when Codex is not visible.
- The app does not send chat messages, does not type commands, does not use `/status`, does not take screenshots or run OCR, and does not move your mouse.

### Privacy & Network Behavior

This companion reads local ChatGPT Codex data and makes one low-frequency network request for reset-credit information. Specifically:

- **Reads local account claims for display only.** It reads the `name` claim from the local `id_token` in `~/.codex/auth.json` to show the signed-in name, falling back to the account email returned by app-server. The token is decoded locally and is never logged or uploaded by this companion.
- **Reads your local access token for reset credits.** To show how many rate-limit reset credits you have, it reads the `access_token` from the same credential file. The token stays in memory only; it is never written to disk and never logged.
- **One reset-credit endpoint with strict throttling.** The access token is used solely as a `Bearer` credential for `https://chatgpt.com/backend-api/wham/rate-limit-reset-credits`. Automatic refresh is attempted at most once per local calendar day. Manual refreshes are user-initiated and enforce a 60-second cooldown between attempts.
- **Local app-server reads.** Context-independent account identity, lifetime token usage, and rate limits are read through local Codex app-server JSON-RPC/stdio calls. These calls do not type into or add content to a conversation.
- **No content is uploaded.** It does not upload chat content, prompts, source code, files, screenshots, or OCR text. It performs no screen capture, OCR, keyboard/mouse automation, or `/status` injection.
- **Unofficial endpoint.** The reset-credit endpoint is an undocumented backend API. It may change or stop working at any time without notice; when a refresh fails, the overlay keeps the last valid cached result when available.
- **Local cache.** Reset-credit results are cached at `%APPDATA%\CodexUsageOverlay\reset-credits-cache.json`. The cache stores only non-sensitive scheduling metadata (number of reset credits and their expiry/grant times). It does **not** store your token or any chat content. Window position and collapse state are stored separately in `%APPDATA%\CodexUsageOverlay\settings.json`.

### Uninstall

1. Close the overlay.
2. Delete the folder where you extracted it (or the startup shortcut, if installed).
3. Optional: delete settings at `%APPDATA%\CodexUsageOverlay\`.

### Share

To share this project, copy the folder without `bin/`, `obj/`, `artifacts/`, `.vs/`, or `.claude/` build, IDE, and local agent outputs.

### License

MIT — see [LICENSE](LICENSE).

---

## 中文

Codex Quota Monitor 是一个**非官方**的 Windows ChatGPT Codex 桌面悬浮窗工具，用来在不打开设置页、不切换上下文的情况下查看常用额度信息。

### 功能

- 最多显示三个竖向圆环：
  - 当前 ChatGPT Codex 会话上下文剩余容量。
  - 5 小时额度剩余（仅在该额度窗口可用时显示）。
  - 周额度剩余。
- 展开模式下显示当前可用额度窗口的刷新时间，以及可用的重置机会次数。
- 展开模式显示当前登录账户名（获取不到时显示邮箱）和累计 Token 数。
- 横向迷你条模式：显示上下文和周额度；5 小时窗口可用时再显示其百分比。
- 托盘模式：优先按 5 小时额度剩余百分比着色；没有 5 小时窗口时改用周额度颜色。
- 颜色含义：绿色 = 剩余 60-100%，黄色 = 20-60%，红色 = 低于 20%，灰色 = 数据不可用。
- 上下文容量只从当前 ChatGPT Codex 会话底部的上下文指示器（UI Automation 文本）读取，不读取提示气泡或聊天内容。
- 从 Codex app-server 的 rate limits 数据读取并按窗口时长识别当前可用额度。
- 通过 foreground WinEvent hook 加低频兜底轮询跟随 ChatGPT 窗口，悬浮窗随 ChatGPT 显示/隐藏。
- 按 Windows 界面语言自动选择中文、英文、法语。
- 支持手动折叠/显示、隐藏到托盘、拖拽、双击隐藏到托盘、记住窗口位置、Windows 登录启动脚本。

### 重要说明

- 这不是 OpenAI 官方产品。
- 本项目不隶属于 OpenAI，也未获得 OpenAI 官方背书或支持。
- 仅支持 Windows。macOS 暂不支持，因为该工具使用 WPF、Win32 窗口钩子和 Windows UI Automation。

### 安装（预编译二进制）

1. 从 [最新 Release](../../releases/latest) 下载 `codex-quota-monitor-win-x64-v0.1.9.zip`。
2. 解压到任意文件夹，例如 `C:\Tools\CodexQuotaMonitor`。
3. 运行 `CodexUsageOverlay.App.exe`。
4. 打开 ChatGPT 并进入 Codex，悬浮窗会自动出现。

该压缩包是 self-contained 版本，不需要额外安装 .NET Desktop Runtime。

### 从源码运行

需要 .NET 10 SDK。在本目录下：

```powershell
.\scripts\Start-CodexUsageOverlay.ps1
```

不重新构建直接启动：

```powershell
.\scripts\Start-CodexUsageOverlay.ps1 -NoBuild
```

### Windows 登录启动

```powershell
.\scripts\Install-StartupShortcut.ps1 -Build   # 安装
.\scripts\Uninstall-StartupShortcut.ps1        # 移除
```

### 使用

- 拖动悬浮窗即可移动位置。
- 点击减号按钮折叠成迷你条；点击 `□` 重新展开。
- 鼠标悬停在迷你条上可以查看额度刷新时间。
- 双击悬浮窗（或点击圆点按钮）隐藏到托盘；点击托盘图标恢复。
- 点击 `x` 关闭悬浮窗。

悬浮窗会记住位置和折叠/展开状态。

### 使用要求

- Windows 10/11 x64
- 已安装支持 Codex 的 ChatGPT 桌面应用
- 已登录 ChatGPT

### 工作方式

- 上下文剩余容量从当前 ChatGPT Codex 界面指示器通过 Windows UI Automation 读取。
- 5 小时额度和周额度从 Codex app-server 的 rate limits 数据通过本地 stdio 读取。
- 悬浮窗会跟随 ChatGPT 窗口显示/隐藏。
- 工具不会发送聊天消息，不会输入命令，不会调用 `/status`，不截图、不做 OCR，也不会移动鼠标。

### 隐私与网络行为

本工具会读取本地 ChatGPT Codex 数据，并对重置机会信息发起低频网络请求：

- **仅读取本地账户声明用于显示。** 工具会从 `~/.codex/auth.json` 的本地 `id_token` 读取 `name` 声明，用来显示账户名；获取不到时回退到 app-server 返回的账户邮箱。该 token 只在本地解码，不记录、不上传。
- **读取本地 access token 获取重置机会。** 为了显示额度重置机会，它会读取同一凭据文件中的 `access_token`。token 只存在于内存中，不落盘、不写日志。
- **单一重置机会接口并严格限频。** access token 仅作为 `Bearer` 凭据，用于请求 `https://chatgpt.com/backend-api/wham/rate-limit-reset-credits`。自动刷新每个本地自然日最多尝试一次；手动刷新仅由用户触发，并强制至少间隔 60 秒。
- **本地 app-server 读取。** 账户身份、累计 Token 用量和额度通过本地 Codex app-server JSON-RPC/stdio 读取，不会向对话框输入内容，也不会增加聊天上下文。
- **不上传任何内容。** 不上传聊天内容、提示词、源码、文件、截图或 OCR 文本；不做屏幕截图、OCR、键鼠模拟或 `/status` 注入。
- **非公开接口。** 重置机会接口是未公开的后端 API，可能随时变更或失效；刷新失败时悬浮窗照常运行，并在存在有效缓存时保留上次结果。
- **本地缓存。** 重置机会结果缓存在 `%APPDATA%\CodexUsageOverlay\reset-credits-cache.json`，只保存非敏感的调度信息（重置次数及到期/发放时间），**不**保存 token 或任何聊天内容。窗口位置与折叠状态单独保存在 `%APPDATA%\CodexUsageOverlay\settings.json`。

### 卸载

1. 关闭悬浮窗。
2. 删除解压出来的文件夹（如安装了启动快捷方式也一并删除）。
3. 可选：删除设置目录 `%APPDATA%\CodexUsageOverlay\`。

### 许可证

MIT —— 见 [LICENSE](LICENSE)。
