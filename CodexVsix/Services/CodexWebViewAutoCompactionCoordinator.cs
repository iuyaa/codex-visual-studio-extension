using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexWebViewAutoCompactionCoordinator : IDisposable
{
    internal const double ContextUsageThreshold = 0.85d;

    private static readonly TimeSpan CompactionCooldown = TimeSpan.FromMinutes(5);

    private readonly object _syncRoot = new();
    private readonly Func<bool> _isEnabled;
    private readonly Func<string, CancellationToken, Task> _compactThreadAsync;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly HashSet<string> _pendingThreads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _compactingThreads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastCompactionUtc = new(StringComparer.Ordinal);
    private bool _disposed;

    public CodexWebViewAutoCompactionCoordinator(
        Func<bool> isEnabled,
        Func<string, CancellationToken, Task> compactThreadAsync,
        Action<string> log)
    {
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _compactThreadAsync = compactThreadAsync ?? throw new ArgumentNullException(nameof(compactThreadAsync));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void ObserveNotification(string method, JToken? parameters)
    {
        if (_disposed || !_isEnabled())
        {
            return;
        }

        if (TryReadContextUsage(method, parameters, out var threadId, out var usedTokens, out var contextWindow)
            && usedTokens > 0
            && contextWindow > 0
            && usedTokens / (double)contextWindow >= ContextUsageThreshold)
        {
            lock (_syncRoot)
            {
                if (!_disposed && !string.IsNullOrWhiteSpace(threadId))
                {
                    _pendingThreads.Add(threadId);
                }
            }

            return;
        }

        if (!IsTurnCompletion(method))
        {
            return;
        }

        threadId = ReadString(parameters, "threadId", "thread_id");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var shouldCompact = false;
        lock (_syncRoot)
        {
            if (_disposed
                || !_pendingThreads.Remove(threadId!)
                || _compactingThreads.Contains(threadId!))
            {
                return;
            }

            if (_lastCompactionUtc.TryGetValue(threadId!, out var last)
                && DateTime.UtcNow - last < CompactionCooldown)
            {
                return;
            }

            _compactingThreads.Add(threadId!);
            shouldCompact = true;
        }

        if (shouldCompact)
        {
            _ = RunCompactionAsync(threadId!);
        }
    }

    internal static bool TryReadContextUsage(
        string method,
        JToken? parameters,
        out string threadId,
        out long usedTokens,
        out long contextWindow)
    {
        threadId = ReadString(parameters, "threadId", "thread_id") ?? string.Empty;
        usedTokens = 0;
        contextWindow = 0;

        if (string.Equals(method, "thread/tokenUsage/updated", StringComparison.Ordinal))
        {
            var usage = parameters?["tokenUsage"];
            usedTokens = ReadLong(usage, "last", "totalTokens")
                ?? ReadLong(usage, "lastTokenUsage", "totalTokens")
                ?? ReadLong(usage, "last_token_usage", "total_tokens")
                ?? ReadLong(usage, "total", "totalTokens")
                ?? ReadLong(usage, "totalTokenUsage", "totalTokens")
                ?? ReadLong(usage, "total_token_usage", "total_tokens")
                ?? 0L;
            contextWindow = ReadLong(usage, "modelContextWindow")
                ?? ReadLong(usage, "model_context_window")
                ?? 0L;
            return !string.IsNullOrWhiteSpace(threadId) && usedTokens > 0 && contextWindow > 0;
        }

        if (string.Equals(method, "codex/event/token_count", StringComparison.Ordinal))
        {
            var info = parameters?["msg"]?["info"];
            usedTokens = ReadLong(info, "last_token_usage", "total_tokens")
                ?? ReadLong(info, "lastTokenUsage", "totalTokens")
                ?? ReadLong(info, "total_token_usage", "total_tokens")
                ?? ReadLong(info, "totalTokenUsage", "totalTokens")
                ?? 0L;
            contextWindow = ReadLong(info, "model_context_window")
                ?? ReadLong(info, "modelContextWindow")
                ?? 0L;
            return !string.IsNullOrWhiteSpace(threadId) && usedTokens > 0 && contextWindow > 0;
        }

        return false;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingThreads.Clear();
        }

        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
    }

    private async Task RunCompactionAsync(string threadId)
    {
        try
        {
            // Let the app-server finish publishing turn/completed before starting a new thread operation.
            await Task.Delay(250, _lifetimeCts.Token).ConfigureAwait(false);
            await _compactThreadAsync(threadId, _lifetimeCts.Token).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _lastCompactionUtc[threadId] = DateTime.UtcNow;
            }

            _log("Automatically compacted a long conversation at 85% context usage.");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                if (!_disposed && _isEnabled())
                {
                    _pendingThreads.Add(threadId);
                }
            }

            _log("Automatic conversation compaction failed: " + ex.Message);
        }
        finally
        {
            lock (_syncRoot)
            {
                _compactingThreads.Remove(threadId);
            }
        }
    }

    private static bool IsTurnCompletion(string method)
    {
        return string.Equals(method, "turn/completed", StringComparison.Ordinal)
            || string.Equals(method, "codex/event/task_complete", StringComparison.Ordinal);
    }

    private static long? ReadLong(JToken? token, params string[] path)
    {
        foreach (var segment in path)
        {
            token = token?[segment];
        }

        return token?.Value<long?>();
    }

    private static string? ReadString(JToken? token, params string[] names)
    {
        if (token is JObject record)
        {
            foreach (var name in names)
            {
                var value = record[name]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            foreach (var property in record.Properties())
            {
                var nested = ReadString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
