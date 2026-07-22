using System;
using System.Collections.Generic;
using System.IO;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexRendererCoordinatorTests
{
    [Fact]
    public void CachedClassicRendererIsRetriedAfterVisualStudioRestarts()
    {
        using var temp = new TemporaryDirectory();
        var store = CreateStore(temp.Path);
        var snapshot = store.Load("fingerprint-a");
        snapshot.PreferredRenderer = CodexRendererCompatibilityStore.ClassicRendererId;
        store.Save(snapshot);

        var coordinator = CreateCoordinator(store, temp.Path);

        Assert.Equal(CodexRendererKind.OfficialWebView, coordinator.CurrentRenderer);
        Assert.Equal(
            CodexRendererCompatibilityStore.ClassicRendererId,
            store.Load("fingerprint-a").PreferredRenderer);
    }

    [Fact]
    public void FallbackDisposesAllOfficialParticipantsBeforeCreatingClassic()
    {
        using var temp = new TemporaryDirectory();
        var store = CreateStore(temp.Path);
        var coordinator = CreateCoordinator(store, temp.Path);
        var sequence = new List<string>();
        coordinator.RendererSwitching += (_, args) =>
        {
            Assert.Equal(CodexRendererKind.OfficialWebView, args.PreviousRenderer);
            Assert.Equal(CodexRendererKind.ClassicWpf, args.NextRenderer);
            sequence.Add("dispose-official");
        };
        coordinator.RendererChanged += (_, _) => sequence.Add("create-classic");

        var changed = coordinator.ReportOfficialFailure("navigation", "blank after docking");
        var changedAgain = coordinator.ReportOfficialFailure("navigation", "must not oscillate");

        Assert.True(changed);
        Assert.False(changedAgain);
        Assert.Equal(new[] { "dispose-official", "create-classic" }, sequence);
        Assert.Equal(CodexRendererKind.ClassicWpf, coordinator.CurrentRenderer);
        Assert.Equal(
            CodexRendererCompatibilityStore.ClassicRendererId,
            store.Load("fingerprint-a").PreferredRenderer);
    }

    [Fact]
    public void ReadyOfficialRendererIsPersistedAsLastKnownGood()
    {
        using var temp = new TemporaryDirectory();
        var store = CreateStore(temp.Path);
        var coordinator = CreateCoordinator(store, temp.Path);

        coordinator.ReportOfficialReady(TimeSpan.FromMilliseconds(420), "windowed");

        var snapshot = store.Load("fingerprint-a");
        Assert.Equal(CodexRendererCompatibilityStore.OfficialRendererId, snapshot.PreferredRenderer);
        Assert.Equal("ready", snapshot.LastOutcome);
        Assert.Equal(420, snapshot.LastReadyDurationMilliseconds);
    }

    [Fact]
    public void ManualRetryRecreatesOfficialRendererInTwoPhases()
    {
        using var temp = new TemporaryDirectory();
        var store = CreateStore(temp.Path);
        var coordinator = CreateCoordinator(store, temp.Path);
        Assert.True(coordinator.ReportOfficialFailure("ready-timeout", "blank interface"));
        var sequence = new List<string>();
        coordinator.RendererSwitching += (_, args) =>
        {
            Assert.Equal(CodexRendererKind.ClassicWpf, args.PreviousRenderer);
            Assert.Equal(CodexRendererKind.OfficialWebView, args.NextRenderer);
            sequence.Add("dispose-classic");
        };
        coordinator.RendererChanged += (_, _) => sequence.Add("create-official");

        var changed = coordinator.RetryOfficialRenderer("manual-main");
        var changedAgain = coordinator.RetryOfficialRenderer("must-not-recreate");

        Assert.True(changed);
        Assert.False(changedAgain);
        Assert.Equal(new[] { "dispose-classic", "create-official" }, sequence);
        Assert.Equal(CodexRendererKind.OfficialWebView, coordinator.CurrentRenderer);
        Assert.Equal("manual-retry", coordinator.GetSnapshotForTests().LastOutcome);
    }

    private static CodexRendererCompatibilityStore CreateStore(string directory)
    {
        return new CodexRendererCompatibilityStore(
            Path.Combine(directory, "renderer.json"),
            "Local\\CodexVsix.CoordinatorTests." + Guid.NewGuid().ToString("N"));
    }

    private static CodexRendererCoordinator CreateCoordinator(
        CodexRendererCompatibilityStore store,
        string directory)
    {
        return new CodexRendererCoordinator(
            store,
            "fingerprint-a",
            new CodexDiagnosticLogger(Path.Combine(directory, "logs")));
    }
}
