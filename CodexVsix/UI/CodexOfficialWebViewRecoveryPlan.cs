using System;
using System.Collections.Generic;

namespace CodexVsix.UI;

internal enum CodexOfficialWebViewProfileKind
{
    Standard,
    Recovery
}

internal sealed class CodexOfficialWebViewRecoveryAttempt
{
    public CodexOfficialWebViewRecoveryAttempt(
        CodexOfficialWebViewHostingMode hostingMode,
        CodexOfficialWebViewProfileKind profileKind)
    {
        HostingMode = hostingMode;
        ProfileKind = profileKind;
    }

    public CodexOfficialWebViewHostingMode HostingMode { get; }

    public CodexOfficialWebViewProfileKind ProfileKind { get; }
}

internal sealed class CodexOfficialWebViewRecoveryPlan
{
    private readonly IReadOnlyList<CodexOfficialWebViewRecoveryAttempt> _attempts;
    private int _currentIndex;

    public CodexOfficialWebViewRecoveryPlan(CodexOfficialWebViewHostingMode initialHostingMode)
    {
        _attempts = initialHostingMode == CodexOfficialWebViewHostingMode.Composition
            ? new[]
            {
                new CodexOfficialWebViewRecoveryAttempt(
                    CodexOfficialWebViewHostingMode.Composition,
                    CodexOfficialWebViewProfileKind.Standard),
                new CodexOfficialWebViewRecoveryAttempt(
                    CodexOfficialWebViewHostingMode.Composition,
                    CodexOfficialWebViewProfileKind.Recovery)
            }
            : new[]
            {
                new CodexOfficialWebViewRecoveryAttempt(
                    CodexOfficialWebViewHostingMode.Windowed,
                    CodexOfficialWebViewProfileKind.Standard),
                new CodexOfficialWebViewRecoveryAttempt(
                    CodexOfficialWebViewHostingMode.Composition,
                    CodexOfficialWebViewProfileKind.Standard),
                new CodexOfficialWebViewRecoveryAttempt(
                    CodexOfficialWebViewHostingMode.Composition,
                    CodexOfficialWebViewProfileKind.Recovery)
            };
    }

    public CodexOfficialWebViewRecoveryAttempt Current => _attempts[_currentIndex];

    public int AttemptNumber => _currentIndex + 1;

    public int AttemptCount => _attempts.Count;

    public bool TryAdvance(out CodexOfficialWebViewRecoveryAttempt nextAttempt)
    {
        if (_currentIndex + 1 >= _attempts.Count)
        {
            nextAttempt = Current;
            return false;
        }

        _currentIndex++;
        nextAttempt = Current;
        return true;
    }

    public static string FormatProfile(CodexOfficialWebViewProfileKind profileKind)
    {
        return profileKind == CodexOfficialWebViewProfileKind.Recovery
            ? "recovery"
            : "standard";
    }
}
