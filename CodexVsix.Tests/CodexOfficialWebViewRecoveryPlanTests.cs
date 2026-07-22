using CodexVsix.UI;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexOfficialWebViewRecoveryPlanTests
{
    [Fact]
    public void WindowedHostFallsBackThroughCompositionAndRecoveryProfile()
    {
        var plan = new CodexOfficialWebViewRecoveryPlan(
            CodexOfficialWebViewHostingMode.Windowed);

        AssertAttempt(
            plan,
            1,
            3,
            CodexOfficialWebViewHostingMode.Windowed,
            CodexOfficialWebViewProfileKind.Standard);
        Assert.True(plan.TryAdvance(out _));
        AssertAttempt(
            plan,
            2,
            3,
            CodexOfficialWebViewHostingMode.Composition,
            CodexOfficialWebViewProfileKind.Standard);
        Assert.True(plan.TryAdvance(out _));
        AssertAttempt(
            plan,
            3,
            3,
            CodexOfficialWebViewHostingMode.Composition,
            CodexOfficialWebViewProfileKind.Recovery);
        Assert.False(plan.TryAdvance(out _));
    }

    [Fact]
    public void ExplicitCompositionModeNeverIntroducesAWindowedHost()
    {
        var plan = new CodexOfficialWebViewRecoveryPlan(
            CodexOfficialWebViewHostingMode.Composition);

        Assert.Equal(CodexOfficialWebViewHostingMode.Composition, plan.Current.HostingMode);
        Assert.True(plan.TryAdvance(out var recovery));
        Assert.Equal(CodexOfficialWebViewHostingMode.Composition, recovery.HostingMode);
        Assert.Equal(CodexOfficialWebViewProfileKind.Recovery, recovery.ProfileKind);
        Assert.False(plan.TryAdvance(out _));
    }

    private static void AssertAttempt(
        CodexOfficialWebViewRecoveryPlan plan,
        int expectedNumber,
        int expectedCount,
        CodexOfficialWebViewHostingMode expectedHostingMode,
        CodexOfficialWebViewProfileKind expectedProfileKind)
    {
        Assert.Equal(expectedNumber, plan.AttemptNumber);
        Assert.Equal(expectedCount, plan.AttemptCount);
        Assert.Equal(expectedHostingMode, plan.Current.HostingMode);
        Assert.Equal(expectedProfileKind, plan.Current.ProfileKind);
    }
}
