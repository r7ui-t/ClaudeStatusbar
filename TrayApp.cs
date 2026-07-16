using System.Diagnostics;

namespace QuotaBar;

/// <summary>
/// Claude と Codex の利用状況をタスクトレイへ表示する本体。
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ClaudeUsageClient _claudeClient = new();
    private readonly CodexUsageClient _codexClient = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Settings _settings;
    private Icon? _currentIcon;
    private bool _disposed;

    private IconStyle _style;
    private UsageSnapshot? _lastClaude;
    private CodexUsageSnapshot? _lastCodex;
    private bool _hasSuccessfulProvider;
    private double _lastDisplayPercent;
    private string _lastSeverity = "normal";

    // Codex メニュー項目
    private readonly ToolStripMenuItem _miCodexStatus;
    private readonly ToolStripMenuItem _miCodexSession;
    private readonly ToolStripMenuItem _miCodexWeekly;
    private readonly ToolStripMenuItem _miCodexPlan;
    private readonly ToolStripMenuItem _miCodexCredits;

    // Claude メニュー項目
    private readonly ToolStripMenuItem _miClaudeStatus;
    private readonly ToolStripMenuItem _miClaudeSession;
    private readonly ToolStripMenuItem _miClaudeWeekly;
    private readonly ToolStripMenuItem _miClaudeScoped;
    private readonly ToolStripMenuItem _miClaudePlan;

    private readonly ToolStripMenuItem _miUpdated;
    private readonly ToolStripMenuItem _miStyleNumber;
    private readonly ToolStripMenuItem _miStyleRing;

    public TrayApp()
    {
        _settings = Settings.Load();
        _style = _settings.Style;

        _miCodexStatus = DisabledItem("読み込み中…");
        _miCodexSession = DisabledItem("5時間: —");
        _miCodexWeekly = DisabledItem("週次: —");
        _miCodexPlan = DisabledItem("プラン: —");
        _miCodexCredits = DisabledItem("クレジット: —");

        _miClaudeStatus = DisabledItem("読み込み中…");
        _miClaudeSession = DisabledItem("5時間: —");
        _miClaudeWeekly = DisabledItem("週次: —");
        _miClaudeScoped = DisabledItem("モデル別週次: —");
        _miClaudeScoped.Visible = false;
        _miClaudePlan = DisabledItem("プラン: —");
        _miUpdated = DisabledItem("更新: —");

        var menu = new ContextMenuStrip();
        menu.Items.Add(DisabledItem("Codex"));
        menu.Items.Add(_miCodexStatus);
        menu.Items.Add(_miCodexSession);
        menu.Items.Add(_miCodexWeekly);
        menu.Items.Add(_miCodexPlan);
        menu.Items.Add(_miCodexCredits);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(DisabledItem("Claude"));
        menu.Items.Add(_miClaudeStatus);
        menu.Items.Add(_miClaudeSession);
        menu.Items.Add(_miClaudeWeekly);
        menu.Items.Add(_miClaudeScoped);
        menu.Items.Add(_miClaudePlan);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miUpdated);
        menu.Items.Add(new ToolStripSeparator());

        // アイコン表示スタイルの切替は既存設定をそのまま利用する。
        var styleMenu = new ToolStripMenuItem("アイコン表示");
        _miStyleNumber = new ToolStripMenuItem("数字（大きく見やすい）");
        _miStyleRing = new ToolStripMenuItem("リング＋数字");
        _miStyleNumber.Click += (_, _) => SelectStyle(IconStyle.Number);
        _miStyleRing.Click += (_, _) => SelectStyle(IconStyle.Ring);
        styleMenu.DropDownItems.Add(_miStyleNumber);
        styleMenu.DropDownItems.Add(_miStyleRing);
        menu.Items.Add(styleMenu);
        UpdateStyleChecks();

        var miRefresh = new ToolStripMenuItem("今すぐ更新");
        miRefresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(miRefresh);

        var credentialsMenu = new ToolStripMenuItem("認証情報フォルダ");
        var miCodexCredentials = new ToolStripMenuItem("Codex（.codex）");
        miCodexCredentials.Click += (_, _) => OpenCredentialsFolder(".codex");
        var miClaudeCredentials = new ToolStripMenuItem("Claude（.claude）");
        miClaudeCredentials.Click += (_, _) => OpenCredentialsFolder(".claude");
        credentialsMenu.DropDownItems.Add(miCodexCredentials);
        credentialsMenu.DropDownItems.Add(miClaudeCredentials);
        menu.Items.Add(credentialsMenu);

        var miQuit = new ToolStripMenuItem("終了");
        miQuit.Click += (_, _) => ExitThread();
        menu.Items.Add(miQuit);

        _currentIcon = IconRenderer.RenderError();
        _tray = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = true,
            Text = "QuotaBar — 起動中",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += async (_, _) => await RefreshAsync();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        // コンストラクターをブロックするとメッセージループが始まらないため、初回取得は非同期で開始する。
        _ = RefreshAsync();
    }

    private static ToolStripMenuItem DisabledItem(string text) =>
        new(text) { Enabled = false };

    private async Task RefreshAsync()
    {
        if (_disposed || !await _refreshGate.WaitAsync(0))
            return;

        try
        {
            // 片方の API の遅延・失敗がもう片方の表示を消さないよう、プロバイダごとに例外を結果へ変換する。
            var claudeTask = FetchClaudeSafelyAsync();
            var codexTask = FetchCodexSafelyAsync();
            await Task.WhenAll(claudeTask, codexTask);

            if (!_disposed)
                UpdateUi(await claudeTask, await codexTask, DateTimeOffset.Now);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<UsageSnapshot> FetchClaudeSafelyAsync()
    {
        try
        {
            return await _claudeClient.FetchAsync();
        }
        catch (Exception ex)
        {
            return UsageSnapshot.Fail($"取得失敗: {ex.Message}");
        }
    }

    private async Task<CodexUsageSnapshot> FetchCodexSafelyAsync()
    {
        try
        {
            return await _codexClient.FetchAsync();
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

    private void UpdateUi(UsageSnapshot claude, CodexUsageSnapshot codex, DateTimeOffset updatedAt)
    {
        _lastClaude = claude;
        _lastCodex = codex;

        UpdateCodexMenu(codex);
        UpdateClaudeMenu(claude);
        _miUpdated.Text = $"更新: {updatedAt:HH:mm:ss}";

        var successfulPercents = new List<double>();
        if (codex.Ok)
            successfulPercents.Add(codex.DisplayPercent);
        if (claude.Ok)
            successfulPercents.Add(claude.DisplayPercent);

        if (successfulPercents.Count == 0)
        {
            _hasSuccessfulProvider = false;
            SwapIcon(IconRenderer.RenderError());
            _tray.Text = BuildTooltip(claude, codex);
            return;
        }

        _hasSuccessfulProvider = true;
        _lastDisplayPercent = successfulPercents.Max();
        _lastSeverity = CalculateSeverity(_lastDisplayPercent, claude);
        SwapIcon(IconRenderer.Render(_lastDisplayPercent, _lastSeverity, _style));
        _tray.Text = BuildTooltip(claude, codex);
    }

    private void UpdateCodexMenu(CodexUsageSnapshot s)
    {
        if (!s.Ok)
        {
            _miCodexStatus.Text = $"⚠ {s.Error ?? "取得に失敗しました。"}";
            _miCodexSession.Text = "5時間: —";
            _miCodexWeekly.Text = "週次: —";
            _miCodexPlan.Text = "プラン: —";
            _miCodexCredits.Text = "クレジット: —";
            _miCodexCredits.Visible = false;
            return;
        }

        object? rateLimitType = s.RateLimitReachedType;
        var rateLimit = rateLimitType?.ToString();
        _miCodexStatus.Text = string.IsNullOrWhiteSpace(rateLimit)
            ? "Codex 利用状況"
            : $"Codex 利用状況（制限: {rateLimit}）";
        _miCodexSession.Text = $"5時間: {Pct(s.SessionPercent)}（リセット {Countdown(s.SessionReset)}）";
        _miCodexWeekly.Text = $"週次: {Pct(s.WeeklyPercent)}（リセット {Countdown(s.WeeklyReset)}）";
        _miCodexPlan.Text = $"プラン: {DisplayValue(s.PlanType)}";

        object? balance = s.CreditBalance;
        if (s.UnlimitedCredits == true)
        {
            _miCodexCredits.Visible = true;
            _miCodexCredits.Text = "クレジット: 無制限";
        }
        else if (s.HasCredits == true && balance is not null)
        {
            _miCodexCredits.Visible = true;
            _miCodexCredits.Text = $"クレジット: 残高 {balance}";
        }
        else
        {
            _miCodexCredits.Visible = false;
        }
    }

    private void UpdateClaudeMenu(UsageSnapshot s)
    {
        if (!s.Ok)
        {
            _miClaudeStatus.Text = $"⚠ {s.Error ?? "取得に失敗しました。"}";
            _miClaudeSession.Text = "5時間: —";
            _miClaudeWeekly.Text = "週次: —";
            _miClaudeScoped.Visible = false;
            _miClaudePlan.Text = "プラン: —";
            return;
        }

        _miClaudeStatus.Text = "Claude 利用状況";
        _miClaudeSession.Text = $"5時間: {Pct(s.SessionPercent)}（リセット {Countdown(s.SessionReset)}）";
        _miClaudeWeekly.Text = $"週次: {Pct(s.WeeklyPercent)}（リセット {Countdown(s.WeeklyReset)}）";

        if (s.ScopedPercent is not null)
        {
            _miClaudeScoped.Visible = true;
            _miClaudeScoped.Text = $"モデル別週次: {Pct(s.ScopedPercent)}";
        }
        else
        {
            _miClaudeScoped.Visible = false;
        }

        _miClaudePlan.Text = $"プラン: {DisplayValue(s.SubscriptionType)}";
    }

    private static string CalculateSeverity(double representativePercent, UsageSnapshot claude)
    {
        var severity = representativePercent >= 95
            ? "critical"
            : representativePercent >= 80
                ? "warning"
                : "normal";

        // Claude API が返した severity が閾値より厳しい場合は安全側へ寄せる。
        return HigherSeverity(severity, claude.Ok ? claude.Severity : null);
    }

    private static string HigherSeverity(string left, string? right) =>
        SeverityRank(right) > SeverityRank(left) ? right! : left;

    private static int SeverityRank(string? severity) => severity switch
    {
        "critical" => 3,
        "warning" => 2,
        "normal" => 1,
        _ => 0,
    };

    // スタイルを選び、直近の代表値を使って即座に再描画して選択を保存する。
    private void SelectStyle(IconStyle style)
    {
        if (_style == style)
            return;

        _style = style;
        UpdateStyleChecks();
        _settings.IconStyle = style.ToString();
        _settings.Save();

        if (_hasSuccessfulProvider)
            SwapIcon(IconRenderer.Render(_lastDisplayPercent, _lastSeverity, _style));
    }

    private void UpdateStyleChecks()
    {
        _miStyleNumber.Checked = _style == IconStyle.Number;
        _miStyleRing.Checked = _style == IconStyle.Ring;
    }

    private void SwapIcon(Icon next)
    {
        _tray.Icon = next;
        _currentIcon?.Dispose();
        _currentIcon = next;
    }

    private static string Pct(double? value) => value is null ? "—" : $"{value.Value:0}%";

    private static string TooltipPct(double? value) => value is null ? "—" : $"{value.Value:0}";

    private static string BuildTooltip(UsageSnapshot claude, CodexUsageSnapshot codex) =>
        Truncate($"C:S{TooltipPct(codex.Ok ? codex.SessionPercent : null)}/W{TooltipPct(codex.Ok ? codex.WeeklyPercent : null)} " +
                 $"A:S{TooltipPct(claude.Ok ? claude.SessionPercent : null)}/W{TooltipPct(claude.Ok ? claude.WeeklyPercent : null)}");

    private static string DisplayValue(object? value) => value?.ToString() ?? "—";

    // リセットまでの残り時間を "2h 15m" 形式で表示する。
    private static string Countdown(DateTimeOffset? reset)
    {
        if (reset is null)
            return "—";

        var span = reset.Value - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero)
            return "まもなく";
        if (span.TotalHours >= 24)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{span.Minutes}m";
    }

    // NotifyIcon.Text は 63 文字までしか設定できない。
    private static string Truncate(string value) => value.Length <= 63 ? value : value[..63];

    private static void OpenCredentialsFolder(string folderName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folderName);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch
        {
            // フォルダが無い等はメインの利用状況表示を妨げない。
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
            // プロセス寿命と同じ小さなセマフォ。取得中に破棄すると finally の Release と競合する。
        }

        base.Dispose(disposing);
    }
}
