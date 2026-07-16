namespace QuotaBar;

internal static class Program
{
    // 多重起動を防ぐための名前付き Mutex
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        // 検証モードはトレイ常駐せず、両プロバイダの非機密な結果だけを一時ファイルへ書く。
        if (args.Contains("--probe"))
        {
            Probe().GetAwaiter().GetResult();
            return;
        }

        // アイコン生成モード: app.ico を指定パスに書き出して終了する。
        if (args.Length >= 2 && args[0] == "--genicon")
        {
            IconFileGenerator.WriteIco(args[1]);
            return;
        }

        _singleInstance = new Mutex(true, "QuotaBar.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApp());

        GC.KeepAlive(_singleInstance);
    }

    private static async Task Probe()
    {
        var claudeTask = FetchClaudeSafelyAsync();
        var codexTask = FetchCodexSafelyAsync();
        await Task.WhenAll(claudeTask, codexTask);
        var claude = await claudeTask;
        var codex = await codexTask;

        var lines = new List<string>
        {
            "Codex",
            $"5時間: {Pct(codex.Ok ? codex.SessionPercent : null)} reset={Reset(codex.Ok ? codex.SessionReset : null)}",
            $"週次: {Pct(codex.Ok ? codex.WeeklyPercent : null)} reset={Reset(codex.Ok ? codex.WeeklyReset : null)}",
            $"エラー: {Error(codex.Ok, codex.Error)}",
            "Claude",
            $"5時間: {Pct(claude.Ok ? claude.SessionPercent : null)} reset={Reset(claude.Ok ? claude.SessionReset : null)}",
            $"週次: {Pct(claude.Ok ? claude.WeeklyPercent : null)} reset={Reset(claude.Ok ? claude.WeeklyReset : null)}",
            $"エラー: {Error(claude.Ok, claude.Error)}",
        };

        var outPath = Path.Combine(Path.GetTempPath(), "quotabar_probe.txt");
        await File.WriteAllLinesAsync(outPath, lines);
    }

    private static async Task<UsageSnapshot> FetchClaudeSafelyAsync()
    {
        try
        {
            return await new ClaudeUsageClient().FetchAsync();
        }
        catch (Exception ex)
        {
            return UsageSnapshot.Fail($"取得失敗: {ex.Message}");
        }
    }

    private static async Task<CodexUsageSnapshot> FetchCodexSafelyAsync()
    {
        try
        {
            return await new CodexUsageClient().FetchAsync();
        }
        catch (Exception ex)
        {
            return new CodexUsageSnapshot
            {
                Ok = false,
                Error = $"取得失敗: {ex.Message}",
                UpdatedAt = DateTimeOffset.Now,
            };
        }
    }

    private static string Pct(double? value) => value is null ? "—" : $"{value.Value:0}%";

    private static string Reset(DateTimeOffset? value) => value?.ToString("o") ?? "—";

    private static string Error(bool ok, string? error) => ok ? "—" : error ?? "取得に失敗しました。";
}
