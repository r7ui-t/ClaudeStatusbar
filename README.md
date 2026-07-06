# ClaudeStatusbar

Claude Code の**利用上限**（セッション/週次）と次のリセットまでの残り時間を、Windows のタスクトレイに一目で表示するアプリです。

## 機能

- トレイアイコンに現在の使用率を表示。severity に応じて色分け（緑 / 黄 / 赤）
- アイコン表示は右クリックメニューから切替可能
  - 数字（大きく見やすい）
  - リング＋数字
- 右クリックメニューで以下を表示
  - セッション使用率（5時間枠）＋リセットまでの残り時間
  - 週次使用率（7日枠）＋リセットまでの残り時間
  - モデル別の週次使用率（存在する場合）
  - プランと最終更新時刻
- 5分ごとに自動更新（アイコンのダブルクリック、または「今すぐ更新」で即時更新）
- 追加ログイン不要（Claude Code が保存済みの OAuth トークンを再利用）
- 単一の `.exe`（self-contained）— .NET ランタイム未導入の PC でも動作

## 動作条件

- Windows 10 / 11
- Claude Code がインストール済みでログイン済み（`%USERPROFILE%\.claude\.credentials.json` が存在すること）

## ビルドと実行

```powershell
# ソースから実行
dotnet run -c Release

# 単一 exe（self-contained）を発行
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

exe は `bin/Release/net8.0-windows/win-x64/publish/ClaudeStatusbar.exe` に生成されます。

## 使い方メモ

- Windows 11 では新規トレイアイコンが既定で隠れているインジケーター（`^`）の中に入ります。常時表示したい場合はタスクバーへドラッグしてください。
- トークンが完全に失効した場合は Claude Code を一度起動すればトークンが更新され、次回ポーリングで自動的に反映されます。
- アイコン表示の選択は `%APPDATA%\ClaudeStatusbar\settings.json` に保存され、次回起動時も維持されます。

## ライセンス

MIT — [LICENSE](LICENSE) を参照。
