# QuotaBar

Claude Code と Codex の利用状況を、Windows のタスクトレイで確認できるアプリです。

## 機能

- Claude と Codex の利用状況を同時に取得して表示
- 5時間枠・週次枠の使用率とリセットまでの残り時間を表示
- Claude のモデル別週次使用率を表示（取得できる場合）
- Codex のプランとクレジット残高を表示（取得できる場合）
- 使用率の高いプロバイダを代表値としてトレイアイコンに表示
- 使用率に応じたアイコンの色分け（通常 / 警告 / 重大）
- アイコン表示を数字またはリング＋数字から選択
- 5分ごとの自動更新、ダブルクリックとメニューからの手動更新
- Codex と Claude の認証情報フォルダをメニューから開く
- 設定を保存し、次回起動時もアイコン表示を維持

## 必要条件

- Windows 10 / 11
- 表示したいプロバイダの CLI がインストール済み・ログイン済み
- Claude または Codex の片方だけでも利用可能
- .NET 8 ランタイム（ソースから実行する場合）

## 発行

追加ランタイム不要の単一 exe を発行できます。

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

発行先は `bin/Release/net8.0-windows/win-x64/publish/QuotaBar.exe` です。
