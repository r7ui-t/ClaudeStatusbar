using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuotaBar;

/// <summary>
/// Codex の利用上限とアカウント情報を UI 向けにまとめたスナップショット。
/// </summary>
public sealed class CodexUsageSnapshot
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public double? SessionPercent { get; init; }
    public double? WeeklyPercent { get; init; }
    public DateTimeOffset? SessionReset { get; init; }
    public DateTimeOffset? WeeklyReset { get; init; }

    public string? PlanType { get; init; }
    public string? CreditBalance { get; init; }
    public bool HasCredits { get; init; }
    public bool UnlimitedCredits { get; init; }
    public string? RateLimitReachedType { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    // セッションと週次のうち、表示上もっとも厳しい利用率を代表値にする。
    public double DisplayPercent => Math.Max(SessionPercent ?? 0, WeeklyPercent ?? 0);

    public static CodexUsageSnapshot Fail(string message) =>
        new() { Ok = false, Error = message };
}

internal sealed class CodexRpcRequest
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }
}

internal sealed class CodexInitializeParams
{
    [JsonPropertyName("clientInfo")]
    public CodexClientInfo ClientInfo { get; init; } = new();
}

internal sealed class CodexClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

internal sealed class CodexRpcEnvelope
{
    [JsonPropertyName("id")]
    public JsonElement? Id { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public CodexRpcError? Error { get; init; }
}

internal sealed class CodexRpcError
{
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class CodexRateLimitsReadResponse
{
    [JsonPropertyName("rateLimits")]
    public CodexRateLimitSnapshot? RateLimits { get; init; }

    [JsonPropertyName("rateLimitsByLimitId")]
    public Dictionary<string, CodexRateLimitSnapshot>? RateLimitsByLimitId { get; init; }
}

internal sealed class CodexRateLimitSnapshot
{
    [JsonPropertyName("primary")]
    public CodexRateLimitWindow? Primary { get; init; }

    [JsonPropertyName("secondary")]
    public CodexRateLimitWindow? Secondary { get; init; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; init; }

    [JsonPropertyName("credits")]
    public CodexCreditsSnapshot? Credits { get; init; }

    [JsonPropertyName("rateLimitReachedType")]
    public string? RateLimitReachedType { get; init; }
}

internal sealed class CodexRateLimitWindow
{
    [JsonPropertyName("usedPercent")]
    public int? UsedPercent { get; init; }

    [JsonPropertyName("resetsAt")]
    public long? ResetsAt { get; init; }

    [JsonPropertyName("windowDurationMins")]
    public long? WindowDurationMins { get; init; }
}

internal sealed class CodexCreditsSnapshot
{
    [JsonPropertyName("balance")]
    public string? Balance { get; init; }

    [JsonPropertyName("hasCredits")]
    public bool HasCredits { get; init; }

    [JsonPropertyName("unlimited")]
    public bool Unlimited { get; init; }
}
