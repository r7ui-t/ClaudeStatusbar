using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeStatusbar;

/// <summary>
/// 取得結果をまとめた表示用スナップショット。
/// UI 側はこの型だけ見ればよいようにしておく（HTTP/JSON の詳細を隠蔽）。
/// </summary>
public sealed class UsageSnapshot
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public double? SessionPercent { get; init; }
    public DateTimeOffset? SessionReset { get; init; }

    public double? WeeklyPercent { get; init; }
    public DateTimeOffset? WeeklyReset { get; init; }

    // weekly_scoped（モデル別。例: Fable の週次）
    public double? ScopedPercent { get; init; }
    public string? ScopedLabel { get; init; }

    public string? Severity { get; init; }
    public string? SubscriptionType { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    // アイコンに出す代表値（セッションと週次の高い方）
    public double DisplayPercent =>
        Math.Max(SessionPercent ?? 0, WeeklyPercent ?? 0);

    public static UsageSnapshot Fail(string message) =>
        new() { Ok = false, Error = message };
}

/// <summary>
/// 認証情報の読み取り → usage エンドポイント呼び出し → パースまで担当。
/// CodexBar の OAuth API 経路（最も単純で確実）を Windows 向けに再実装したもの。
/// </summary>
public sealed class ClaudeUsageClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string OauthBeta = "oauth-2025-04-20";

    // 認証情報ファイル。Windows では ~ が %USERPROFILE% に対応
    private static string CredentialsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");

    private readonly HttpClient _http;

    public ClaudeUsageClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct = default)
    {
        // 毎回ファイルから読み直すのが肝。Claude Code 本体がトークンを
        // 自動更新してファイルに書き戻すので、常に最新の access token を使える。
        OauthCreds creds;
        try
        {
            if (!File.Exists(CredentialsPath))
                return UsageSnapshot.Fail("認証情報が見つかりません。Claude Code にログインしてください。");

            var json = await File.ReadAllTextAsync(CredentialsPath, ct);
            var file = JsonSerializer.Deserialize<CredentialsFile>(json);
            if (file?.ClaudeAiOauth?.AccessToken is null)
                return UsageSnapshot.Fail("認証情報の形式が不正です。");
            creds = file.ClaudeAiOauth;
        }
        catch (Exception ex)
        {
            return UsageSnapshot.Fail($"認証情報の読み取り失敗: {ex.Message}");
        }

        if (!creds.HasProfileScope)
            return UsageSnapshot.Fail("user:profile スコープがありません。再ログインが必要です。");

        // レスポンス取得
        HttpResponseMessage resp;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
            req.Headers.Add("anthropic-beta", OauthBeta);
            resp = await _http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            return UsageSnapshot.Fail($"通信エラー: {ex.Message}");
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return UsageSnapshot.Fail("トークン期限切れ。Claude Code を一度起動して更新してください。");

        if (!resp.IsSuccessStatusCode)
            return UsageSnapshot.Fail($"APIエラー: HTTP {(int)resp.StatusCode}");

        UsageResponse? data;
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            data = JsonSerializer.Deserialize<UsageResponse>(body);
        }
        catch (Exception ex)
        {
            return UsageSnapshot.Fail($"応答の解析失敗: {ex.Message}");
        }

        if (data is null)
            return UsageSnapshot.Fail("応答が空です。");

        return Map(data, creds.SubscriptionType);
    }

    // limits[] を優先し、無ければ five_hour / seven_day にフォールバック
    private static UsageSnapshot Map(UsageResponse data, string? subscriptionType)
    {
        LimitItem? session = data.Limits?.FirstOrDefault(l => l.Kind == "session");
        LimitItem? weeklyAll = data.Limits?.FirstOrDefault(l => l.Kind == "weekly_all");
        LimitItem? scoped = data.Limits?.FirstOrDefault(l => l.Kind == "weekly_scoped");

        double? sessionPct = session?.Percent ?? data.FiveHour?.Utilization;
        DateTimeOffset? sessionReset = session?.ResetsAt ?? data.FiveHour?.ResetsAt;

        double? weeklyPct = weeklyAll?.Percent ?? data.SevenDay?.Utilization;
        DateTimeOffset? weeklyReset = weeklyAll?.ResetsAt ?? data.SevenDay?.ResetsAt;

        // severity は最も厳しいものを代表に
        string severity = new[] { session?.Severity, weeklyAll?.Severity, scoped?.Severity }
            .Where(s => s != null)
            .OrderByDescending(SeverityRank)
            .FirstOrDefault() ?? "normal";

        return new UsageSnapshot
        {
            Ok = true,
            SessionPercent = sessionPct,
            SessionReset = sessionReset,
            WeeklyPercent = weeklyPct,
            WeeklyReset = weeklyReset,
            ScopedPercent = scoped?.Percent,
            ScopedLabel = scoped?.Scope?.Model?.DisplayName,
            Severity = severity,
            SubscriptionType = subscriptionType,
        };
    }

    private static int SeverityRank(string? s) => s switch
    {
        "critical" => 3,
        "warning" => 2,
        "normal" => 1,
        _ => 0,
    };
}

