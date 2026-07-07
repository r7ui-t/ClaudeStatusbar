namespace ClaudeStatusbar;

internal static class Program
{
    // 多重起動を防ぐための名前付き Mutex
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        // 検証モード: データ取得だけ行い結果をファイルに書いて終了（トレイ常駐しない）
        if (args.Contains("--probe"))
        {
            Probe().GetAwaiter().GetResult();
            return;
        }

        // アイコン生成モード: app.ico を指定パスに書き出して終了（ビルド前の資産生成用）
        if (args.Length >= 2 && args[0] == "--genicon")
        {
            IconFileGenerator.WriteIco(args[1]);
            return;
        }

        _singleInstance = new Mutex(true, "ClaudeStatusbar.SingleInstance", out bool isNew);
        if (!isNew)
            return; // 既に起動済みなら何もせず終了

        // 高DPI対応（トレイアイコンやメニューのにじみ防止）
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApp());

        GC.KeepAlive(_singleInstance);
    }

    private static async Task Probe()
    {
        var snap = await new ClaudeUsageClient().FetchAsync();
        var lines = new List<string>
        {
            $"Ok={snap.Ok}",
            $"Error={snap.Error}",
            $"Session={snap.SessionPercent}% reset={snap.SessionReset:o}",
            $"Weekly={snap.WeeklyPercent}% reset={snap.WeeklyReset:o}",
            $"Scoped={snap.ScopedPercent}% ({snap.ScopedLabel})",
            $"Severity={snap.Severity}",
            $"Plan={snap.SubscriptionType}",
            $"Display={snap.DisplayPercent}%",
        };
        var outPath = Path.Combine(Path.GetTempPath(), "claudestatusbar_probe.txt");
        await File.WriteAllLinesAsync(outPath, lines);
    }
}

