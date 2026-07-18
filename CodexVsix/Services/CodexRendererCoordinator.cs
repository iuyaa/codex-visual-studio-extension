using System;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexRendererTransitionEventArgs : EventArgs
{
    public CodexRendererTransitionEventArgs(
        CodexRendererKind previousRenderer,
        CodexRendererKind nextRenderer,
        string reason)
    {
        PreviousRenderer = previousRenderer;
        NextRenderer = nextRenderer;
        Reason = reason ?? string.Empty;
    }

    public CodexRendererKind PreviousRenderer { get; }

    public CodexRendererKind NextRenderer { get; }

    public string Reason { get; }
}

internal sealed class CodexRendererCoordinator
{
    private readonly object _syncRoot = new();
    private readonly CodexRendererCompatibilityStore _store;
    private readonly CodexDiagnosticLogger _diagnostics;
    private readonly string _environmentFingerprint;
    private CodexRendererCompatibilitySnapshot _snapshot;
    private CodexRendererKind _currentRenderer;

    public CodexRendererCoordinator()
        : this(
            new CodexRendererCompatibilityStore(),
            CodexRendererEnvironmentFingerprint.CreateCurrent(),
            CodexDiagnosticLogger.Shared)
    {
    }

    internal CodexRendererCoordinator(
        CodexRendererCompatibilityStore store,
        string environmentFingerprint,
        CodexDiagnosticLogger diagnostics)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _environmentFingerprint = string.IsNullOrWhiteSpace(environmentFingerprint)
            ? "unknown"
            : environmentFingerprint.Trim();
        _snapshot = _store.Load(_environmentFingerprint);
        _currentRenderer = CodexRendererCompatibilityStore.ParseRenderer(_snapshot.PreferredRenderer);

        _diagnostics.Write(
            "renderer.decision",
            new JObject
            {
                ["renderer"] = CodexRendererCompatibilityStore.FormatRenderer(_currentRenderer),
                ["cacheMatched"] = string.IsNullOrWhiteSpace(_store.LastLoadError),
                ["cacheError"] = _store.LastLoadError
            });
    }

    public static CodexRendererCoordinator Shared { get; } = new();

    public CodexRendererKind CurrentRenderer
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentRenderer;
            }
        }
    }

    public event EventHandler<CodexRendererTransitionEventArgs>? RendererSwitching;

    public event EventHandler<CodexRendererTransitionEventArgs>? RendererChanged;

    public void ReportOfficialReady(TimeSpan readyDuration, string hostingMode)
    {
        CodexRendererCompatibilitySnapshot snapshot;
        lock (_syncRoot)
        {
            if (_currentRenderer != CodexRendererKind.OfficialWebView)
            {
                return;
            }

            _snapshot.PreferredRenderer = CodexRendererCompatibilityStore.OfficialRendererId;
            _snapshot.LastOutcome = "ready";
            _snapshot.LastReason = string.Empty;
            _snapshot.LastReadyDurationMilliseconds = Math.Max(0, (long)readyDuration.TotalMilliseconds);
            _snapshot.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");
            snapshot = CloneSnapshot(_snapshot);
        }

        TrySave(snapshot);
        _diagnostics.Write(
            "renderer.official.ready",
            new JObject
            {
                ["hostingMode"] = hostingMode ?? string.Empty,
                ["durationMilliseconds"] = snapshot.LastReadyDurationMilliseconds
            });
    }

    public bool ReportOfficialFailure(string failureKind, string reason)
    {
        CodexRendererTransitionEventArgs transition;
        CodexRendererCompatibilitySnapshot snapshot;
        lock (_syncRoot)
        {
            if (_currentRenderer != CodexRendererKind.OfficialWebView)
            {
                return false;
            }

            transition = new CodexRendererTransitionEventArgs(
                CodexRendererKind.OfficialWebView,
                CodexRendererKind.ClassicWpf,
                reason);
            _currentRenderer = CodexRendererKind.ClassicWpf;
            _snapshot.PreferredRenderer = CodexRendererCompatibilityStore.ClassicRendererId;
            _snapshot.LastOutcome = string.IsNullOrWhiteSpace(failureKind)
                ? "official-failure"
                : failureKind.Trim();
            _snapshot.LastReason = reason ?? string.Empty;
            _snapshot.LastReadyDurationMilliseconds = null;
            _snapshot.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");
            snapshot = CloneSnapshot(_snapshot);
        }

        _diagnostics.Write(
            "renderer.official.fallback",
            new JObject
            {
                ["failureKind"] = failureKind ?? string.Empty,
                ["reason"] = reason ?? string.Empty,
                ["nextRenderer"] = CodexRendererCompatibilityStore.ClassicRendererId
            });

        // This is intentionally two phase: every participant disposes its WebView first,
        // and only then may any participant construct the classic WPF renderer.
        RaiseSafely(RendererSwitching, transition);
        TrySave(snapshot);
        RaiseSafely(RendererChanged, transition);
        return true;
    }

    internal CodexRendererCompatibilitySnapshot GetSnapshotForTests()
    {
        lock (_syncRoot)
        {
            return CloneSnapshot(_snapshot);
        }
    }

    private void TrySave(CodexRendererCompatibilitySnapshot snapshot)
    {
        try
        {
            _store.Save(snapshot);
        }
        catch (Exception ex)
        {
            _diagnostics.Write(
                "renderer.cache.save-failed",
                new JObject { ["error"] = ex.Message });
        }
    }

    private void RaiseSafely(
        EventHandler<CodexRendererTransitionEventArgs>? handler,
        CodexRendererTransitionEventArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<CodexRendererTransitionEventArgs> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                _diagnostics.Write(
                    "renderer.transition-handler.failed",
                    new JObject { ["error"] = ex.Message });
            }
        }
    }

    private static CodexRendererCompatibilitySnapshot CloneSnapshot(
        CodexRendererCompatibilitySnapshot snapshot)
    {
        return new CodexRendererCompatibilitySnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            RendererPolicyVersion = snapshot.RendererPolicyVersion,
            EnvironmentFingerprint = snapshot.EnvironmentFingerprint,
            PreferredRenderer = snapshot.PreferredRenderer,
            LastOutcome = snapshot.LastOutcome,
            LastReason = snapshot.LastReason,
            LastReadyDurationMilliseconds = snapshot.LastReadyDurationMilliseconds,
            UpdatedUtc = snapshot.UpdatedUtc
        };
    }
}
