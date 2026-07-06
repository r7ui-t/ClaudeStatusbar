# ClaudeStatusbar

A Windows system-tray app that shows your **Claude Code usage limits** at a glance — session and weekly windows, with a countdown to the next reset.

## Features

- Ring-gauge tray icon showing the current usage percentage, color-coded by severity (green / amber / red)
- Right-click menu with:
  - Session usage (5-hour window) + reset countdown
  - Weekly usage (7-day window) + reset countdown
  - Model-scoped weekly usage (per-model limit) when present
  - Subscription plan and last-updated time
- Auto-refresh every 5 minutes; double-click the icon or "Refresh now" for on-demand updates
- No extra login — reuses the OAuth token Claude Code already stores locally
- Single self-contained `.exe` — runs on machines without the .NET runtime installed

## Requirements

- Windows 10/11
- Claude Code installed and logged in (so `%USERPROFILE%\.claude\.credentials.json` exists)

## Build & run

```powershell
# run from source
dotnet run -c Release

# produce a single self-contained exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The exe lands in `bin/Release/net8.0-windows/win-x64/publish/ClaudeStatusbar.exe`.

## Usage notes

- The tray icon lives in the Windows 11 hidden-icons overflow (the `^` chevron) by default. Drag it onto the taskbar to keep it visible.
- If the token fully expires, launch Claude Code once to refresh it; the app picks up the new token on the next poll.

## License

MIT — see [LICENSE](LICENSE).

---

## 日本語

Claude Code の利用上限（セッション/週次）と次のリセットまでの残り時間を Windows のタスクトレイに表示するアプリです。

- トレイのリングゲージに使用率を色分け表示（緑/黄/赤）
- 右クリックメニューでセッション/週次の使用率・リセット残り時間・プラン・最終更新を表示
- 5分ごと自動更新（アイコンのダブルクリックで即時更新）
- 追加ログイン不要（Claude Code が保存済みの OAuth トークンを再利用）
- .NET ランタイム未導入のPCでも動く単一 exe

### 動作条件

- Windows 10/11
- Claude Code がインストール済みでログイン済み（`%USERPROFILE%\.claude\.credentials.json` が存在すること）
