using System.Diagnostics;

namespace ClaudeStatusbar;

/// <summary>
/// トレイ常駐の本体。ApplicationContext を継承すると Form を持たずに
/// メッセージループを回せる（トレイアプリの定石）。
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ClaudeUsageClient _client = new();
    private Icon? _currentIcon;

    // メニュー項目（都度作り直さず更新する）
    private readonly ToolStripMenuItem _miStatus;
    private readonly ToolStripMenuItem _miSession;
    private readonly ToolStripMenuItem _miWeekly;
    private readonly ToolStripMenuItem _miScoped;
    private readonly ToolStripMenuItem _miPlan;
    private readonly ToolStripMenuItem _miUpdated;

    public TrayApp()
    {
        _miStatus = new ToolStripMenuItem("読み込み中…") { Enabled = false };
        _miSession = new ToolStripMenuItem("セッション: —") { Enabled = false };
        _miWeekly = new ToolStripMenuItem("週次: —") { Enabled = false };
        _miScoped = new ToolStripMenuItem("週次(モデル別): —") { Enabled = false, Visible = false };
        _miPlan = new ToolStripMenuItem("プラン: —") { Enabled = false };
        _miUpdated = new ToolStripMenuItem("更新: —") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_miStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miSession);
        menu.Items.Add(_miWeekly);
        menu.Items.Add(_miScoped);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miPlan);
        menu.Items.Add(_miUpdated);
        menu.Items.Add(new ToolStripSeparator());

        var miRefresh = new ToolStripMenuItem("今すぐ更新");
        miRefresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(miRefresh);

        var miOpen = new ToolStripMenuItem("認証情報フォルダを開く");
        miOpen.Click += (_, _) => OpenCredentialsFolder();
        menu.Items.Add(miOpen);

        var miQuit = new ToolStripMenuItem("終了");
        miQuit.Click += (_, _) => ExitThread();
        menu.Items.Add(miQuit);

        _currentIcon = IconRenderer.RenderError();
        _tray = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = true,
            Text = "Claude Statusbar — 起動中",
            ContextMenuStrip = menu,
        };
        // 左ダブルクリックでも手動更新
        _tray.DoubleClick += async (_, _) => await RefreshAsync();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        // 起動直後に一度取得
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        UsageSnapshot snap;
        try
        {
            snap = await _client.FetchAsync();
        }
        catch (Exception ex)
        {
            snap = UsageSnapshot.Fail(ex.Message);
        }
        UpdateUi(snap);
    }

    private void UpdateUi(UsageSnapshot s)
    {
        if (!s.Ok)
        {
            SwapIcon(IconRenderer.RenderError());
            _tray.Text = Truncate($"Claude Statusbar — エラー");
            _miStatus.Text = $"⚠ {s.Error}";
            _miSession.Text = "セッション: —";
            _miWeekly.Text = "週次: —";
            _miScoped.Visible = false;
            _miPlan.Text = "プラン: —";
            _miUpdated.Text = $"更新: {s.UpdatedAt:HH:mm:ss}";
            return;
        }

        SwapIcon(IconRenderer.Render(s.DisplayPercent, s.Severity ?? "normal"));

        string sessionLine = $"セッション: {Pct(s.SessionPercent)}  (リセット {Countdown(s.SessionReset)})";
        string weeklyLine = $"週次: {Pct(s.WeeklyPercent)}  (リセット {Countdown(s.WeeklyReset)})";

        _miStatus.Text = "Claude Code 利用状況";
        _miSession.Text = sessionLine;
        _miWeekly.Text = weeklyLine;

        if (s.ScopedPercent is not null)
        {
            _miScoped.Visible = true;
            _miScoped.Text = $"週次({s.ScopedLabel ?? "モデル別"}): {Pct(s.ScopedPercent)}";
        }
        else
        {
            _miScoped.Visible = false;
        }

        _miPlan.Text = $"プラン: {s.SubscriptionType ?? "—"}";
        _miUpdated.Text = $"更新: {s.UpdatedAt:HH:mm:ss}";

        // ツールチップ（マウスホバー時）。64文字制限に注意
        _tray.Text = Truncate($"Claude  S:{Pct(s.SessionPercent)} / W:{Pct(s.WeeklyPercent)}");
    }

    private void SwapIcon(Icon next)
    {
        _tray.Icon = next;
        _currentIcon?.Dispose(); // 前のアイコンの GDI ハンドルを解放
        _currentIcon = next;
    }

    private static string Pct(double? v) => v is null ? "—" : $"{v.Value:0}%";

    // リセットまでの残り時間を "2h 15m" 形式で
    private static string Countdown(DateTimeOffset? reset)
    {
        if (reset is null) return "—";
        var span = reset.Value - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero) return "まもなく";
        if (span.TotalHours >= 24)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{span.Minutes}m";
    }

    // NotifyIcon.Text は 63 文字までしか設定できない
    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    private static void OpenCredentialsFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* フォルダが無い等は無視 */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
