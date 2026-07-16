using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace QuotaBar;

/// <summary>
/// Codex CLI の app-server stdio API から利用上限を取得するクライアント。
/// 認証情報ファイルやトークンには触れず、CLI に認証処理を委ねる。
/// </summary>
public sealed class CodexUsageClient
{
    private const int InitializeRequestId = 1;
    private const int RateLimitsRequestId = 2;
    private const int TimeoutSeconds = 15;
    private const int CommandNotFoundExitCode = 9009;
    private const long SessionWindowMinutes = 300;
    private const long WeeklyWindowMinutes = 10080;

    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<CodexUsageSnapshot> FetchAsync(CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Process? process = null;
        try
        {
            process = StartAppServer();

            // stderr はパイプ詰まりを防ぐため読み捨てる。内容は保持も表示もしない。
            _ = DrainStandardErrorAsync(process.StandardError);

            await SendAsync(
                process.StandardInput,
                new CodexRpcRequest
                {
                    Id = InitializeRequestId,
                    Method = "initialize",
                    Params = new CodexInitializeParams
                    {
                        ClientInfo = new CodexClientInfo
                        {
                            Name = "QuotaBar",
                            Version = "0.1.0",
                        },
                    },
                },
                operationCts.Token);

            var initializeResponse = await ReadResponseAsync(
                process.StandardOutput,
                InitializeRequestId,
                operationCts.Token);
            EnsureSuccessfulResponse(initializeResponse);

            await SendAsync(
                process.StandardInput,
                new CodexRpcRequest { Method = "initialized" },
                operationCts.Token);

            await SendAsync(
                process.StandardInput,
                new CodexRpcRequest
                {
                    Id = RateLimitsRequestId,
                    Method = "account/rateLimits/read",
                },
                operationCts.Token);

            var rateLimitsResponse = await ReadResponseAsync(
                process.StandardOutput,
                RateLimitsRequestId,
                operationCts.Token);
            EnsureSuccessfulResponse(rateLimitsResponse);

            if (!rateLimitsResponse.Result.HasValue ||
                rateLimitsResponse.Result.Value.ValueKind != JsonValueKind.Object)
            {
                throw new CodexProtocolException();
            }

            var data = JsonSerializer.Deserialize<CodexRateLimitsReadResponse>(
                rateLimitsResponse.Result.Value.GetRawText(),
                JsonOptions);
            if (data is null)
                throw new CodexProtocolException();

            var snapshot = SelectSnapshot(data);
            if (snapshot is null)
                throw new CodexProtocolException();

            return CreateSnapshot(snapshot);
        }
        catch (CodexProtocolException) when (IsCodexCommandMissing(process))
        {
            return CodexUsageSnapshot.Fail(
                "codex コマンドが見つかりません。Codex CLI をインストールしてください。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CodexUsageSnapshot.Fail("Codex 利用上限の取得がキャンセルされました。");
        }
        catch (OperationCanceledException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex app-server からの応答がタイムアウトしました（15秒）。");
        }
        catch (CodexAuthenticationException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex CLI にログインしていません。Codex CLI でログインしてください。");
        }
        catch (JsonException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex app-server の JSON 応答を解析できませんでした。");
        }
        catch (CodexProtocolException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex app-server のプロトコル応答が不正です。");
        }
        catch (Win32Exception)
        {
            return CodexUsageSnapshot.Fail("cmd.exe を起動できませんでした。");
        }
        catch (IOException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex app-server との通信でエラーが発生しました。");
        }
        catch (InvalidOperationException)
        {
            return CodexUsageSnapshot.Fail(
                "Codex app-server のプロトコルを初期化できませんでした。");
        }
        catch (Exception)
        {
            // 予期しない例外の原文には環境情報が含まれ得るため、表示へ渡さない。
            return CodexUsageSnapshot.Fail(
                "Codex 利用上限の取得で予期しないエラーが発生しました。");
        }
        finally
        {
            await StopProcessAsync(process);
        }
    }

    private static Process StartAppServer()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /s /c \"codex app-server --stdio\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException();

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static bool IsCodexCommandMissing(Process? process)
    {
        try
        {
            return process is not null && process.HasExited &&
                   process.ExitCode == CommandNotFoundExitCode;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task SendAsync(
        StreamWriter input,
        CodexRpcRequest request,
        CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(request, JsonOptions);
        await input.WriteLineAsync(line.AsMemory(), ct);
        await input.FlushAsync(ct);
    }

    private static async Task<CodexRpcEnvelope> ReadResponseAsync(
        StreamReader output,
        int expectedId,
        CancellationToken ct)
    {
        while (true)
        {
            var line = await output.ReadLineAsync(ct);
            if (line is null)
                throw new CodexProtocolException();

            if (line.Length > 0 && line[0] == '\uFEFF')
                line = line[1..];

            var message = JsonSerializer.Deserialize<CodexRpcEnvelope>(line, JsonOptions)
                ?? throw new CodexProtocolException();

            if (!IsRequestId(message.Id, expectedId))
                continue;

            if (!message.Result.HasValue && message.Error is null)
                throw new CodexProtocolException();

            return message;
        }
    }

    private static bool IsRequestId(JsonElement? id, int expectedId)
    {
        return id.HasValue &&
               id.Value.ValueKind == JsonValueKind.Number &&
               id.Value.TryGetInt32(out var actualId) &&
               actualId == expectedId;
    }

    private static void EnsureSuccessfulResponse(CodexRpcEnvelope response)
    {
        if (response.Error is null)
            return;

        if (IsAuthenticationError(response.Error))
            throw new CodexAuthenticationException();

        throw new CodexProtocolException();
    }

    private static bool IsAuthenticationError(CodexRpcError error)
    {
        if (error.Code is 401 or 403)
            return true;

        var message = error.Message;
        return message is not null &&
               (message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("logged in", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("未ログイン", StringComparison.Ordinal) ||
                message.Contains("ログイン", StringComparison.Ordinal));
    }

    private static CodexRateLimitSnapshot? SelectSnapshot(CodexRateLimitsReadResponse data)
    {
        if (data.RateLimitsByLimitId?.TryGetValue("codex", out var codex) == true)
            return codex;

        return data.RateLimits;
    }

    private static CodexUsageSnapshot CreateSnapshot(CodexRateLimitSnapshot data)
    {
        var (session, weekly) = SelectWindows(data.Primary, data.Secondary);

        return new CodexUsageSnapshot
        {
            Ok = true,
            SessionPercent = session?.UsedPercent,
            WeeklyPercent = weekly?.UsedPercent,
            SessionReset = ToDateTimeOffset(session?.ResetsAt),
            WeeklyReset = ToDateTimeOffset(weekly?.ResetsAt),
            PlanType = data.PlanType,
            CreditBalance = data.Credits?.Balance,
            HasCredits = data.Credits?.HasCredits ?? false,
            UnlimitedCredits = data.Credits?.Unlimited ?? false,
            RateLimitReachedType = data.RateLimitReachedType,
        };
    }

    private static (CodexRateLimitWindow? Session, CodexRateLimitWindow? Weekly) SelectWindows(
        CodexRateLimitWindow? primary,
        CodexRateLimitWindow? secondary)
    {
        var primaryDuration = primary?.WindowDurationMins;
        var secondaryDuration = secondary?.WindowDurationMins;

        CodexRateLimitWindow? session = null;
        CodexRateLimitWindow? weekly = null;

        if (primaryDuration.HasValue && secondaryDuration.HasValue)
        {
            // 2つとも duration がある場合は、個別の近さが衝突しても
            // 2通りの割り当て全体で距離が最小になる組み合わせを選ぶ。
            var primaryAsSession = Distance(primaryDuration.Value, SessionWindowMinutes);
            var primaryAsWeekly = Distance(primaryDuration.Value, WeeklyWindowMinutes);
            var secondaryAsSession = Distance(secondaryDuration.Value, SessionWindowMinutes);
            var secondaryAsWeekly = Distance(secondaryDuration.Value, WeeklyWindowMinutes);

            var primarySessionAssignment = primaryAsSession + secondaryAsWeekly;
            var secondarySessionAssignment = primaryAsWeekly + secondaryAsSession;

            if (primarySessionAssignment <= secondarySessionAssignment)
            {
                session = primary;
                weekly = secondary;
            }
            else
            {
                session = secondary;
                weekly = primary;
            }

            return (session, weekly);
        }

        // duration がある window は、その値だけで個別分類する。
        // これにより primary=10080・secondary=null でも週次へ置ける。
        if (primaryDuration.HasValue)
            AssignByDuration(primary, primaryDuration.Value, ref session, ref weekly);

        if (secondaryDuration.HasValue)
            AssignByDuration(secondary, secondaryDuration.Value, ref session, ref weekly);

        // duration 欠落分だけ、空いている枠へ従来の primary/secondary 優先順で置く。
        if (!primaryDuration.HasValue)
            AssignFallback(primary, preferSession: true, ref session, ref weekly);

        if (!secondaryDuration.HasValue)
            AssignFallback(secondary, preferSession: false, ref session, ref weekly);

        return (session, weekly);
    }

    private static void AssignByDuration(
        CodexRateLimitWindow? window,
        long duration,
        ref CodexRateLimitWindow? session,
        ref CodexRateLimitWindow? weekly)
    {
        if (window is null)
            return;

        if (Distance(duration, SessionWindowMinutes) <=
            Distance(duration, WeeklyWindowMinutes))
        {
            session = window;
        }
        else
        {
            weekly = window;
        }
    }

    private static void AssignFallback(
        CodexRateLimitWindow? window,
        bool preferSession,
        ref CodexRateLimitWindow? session,
        ref CodexRateLimitWindow? weekly)
    {
        if (window is null)
            return;

        if (preferSession)
        {
            if (session is null)
                session = window;
            else if (weekly is null)
                weekly = window;
        }
        else
        {
            if (weekly is null)
                weekly = window;
            else if (session is null)
                session = window;
        }
    }

    private static double Distance(long duration, long target) =>
        Math.Abs((double)duration - target);

    private static DateTimeOffset? ToDateTimeOffset(long? unixSeconds)
    {
        if (!unixSeconds.HasValue)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static async Task DrainStandardErrorAsync(StreamReader error)
    {
        try
        {
            while (await error.ReadLineAsync() is not null)
            {
                // stderr の内容は認証情報などを含む可能性があるため、記録しない。
            }
        }
        catch (IOException)
        {
            // プロセス終了時のパイプ切断は取得結果に影響させない。
        }
        catch (ObjectDisposedException)
        {
            // プロセス終了後の破棄競合は無視する。
        }
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Start 前または終了済みの場合は後処理不要。
        }
        catch (Win32Exception)
        {
            // 終了競合時も Dispose まで進める。
        }

        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // Start 前の場合は待機できない。
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class CodexProtocolException : Exception
    {
    }

    private sealed class CodexAuthenticationException : Exception
    {
    }
}
