using System.Text.Json.Serialization;

namespace ClaudeStatusbar;

// ─────────────────────────────────────────────────────────────
// 認証情報ファイル (%USERPROFILE%\.claude\.credentials.json) の形
// Claude Code 本体が書き出し・自動更新する。CodexBar と同じくこれを再利用する
// ─────────────────────────────────────────────────────────────
public class CredentialsFile
{
    [JsonPropertyName("claudeAiOauth")]
    public OauthCreds? ClaudeAiOauth { get; set; }
}

public class OauthCreds
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    // epoch ミリ秒。トークンの有効期限
    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("subscriptionType")]
    public string? SubscriptionType { get; set; }

    // usage エンドポイントには user:profile スコープが必要
    public bool HasProfileScope => Scopes?.Contains("user:profile") == true;

    public DateTimeOffset ExpiresAtLocal =>
        DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAt).ToLocalTime();
}

// ─────────────────────────────────────────────────────────────
// GET https://api.anthropic.com/api/oauth/usage のレスポンス
// limits[] 配列が一番扱いやすいので主に使い、五時間/週次は補助
// ─────────────────────────────────────────────────────────────
public class UsageResponse
{
    [JsonPropertyName("limits")]
    public List<LimitItem>? Limits { get; set; }

    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; set; }
}

public class LimitItem
{
    // "session" / "weekly_all" / "weekly_scoped"
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    // "session" / "weekly"
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    // "normal" / "warning" / "critical" など
    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }

    [JsonPropertyName("scope")]
    public LimitScope? Scope { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}

public class LimitScope
{
    [JsonPropertyName("model")]
    public ScopeModel? Model { get; set; }
}

public class ScopeModel
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

public class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }
}
