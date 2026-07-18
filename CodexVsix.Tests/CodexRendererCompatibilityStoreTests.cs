using System;
using System.IO;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexRendererCompatibilityStoreTests
{
    [Fact]
    public void MissingCacheStartsWithOfficialRendererWithoutWritingAFile()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "renderer.json");
        var store = CreateStore(file);

        var snapshot = store.Load("fingerprint-a");

        Assert.Equal(CodexRendererCompatibilityStore.OfficialRendererId, snapshot.PreferredRenderer);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ClassicFallbackPersistsForTheSameEnvironmentFingerprint()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "renderer.json");
        var store = CreateStore(file);
        var snapshot = store.Load("fingerprint-a");
        snapshot.PreferredRenderer = CodexRendererCompatibilityStore.ClassicRendererId;
        snapshot.LastOutcome = "process";
        snapshot.LastReason = "browser exited";
        snapshot.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");

        store.Save(snapshot);
        var loaded = store.Load("fingerprint-a");

        Assert.Equal(CodexRendererCompatibilityStore.ClassicRendererId, loaded.PreferredRenderer);
        Assert.Equal("process", loaded.LastOutcome);
    }

    [Fact]
    public void ChangedEnvironmentFingerprintRetriesOfficialRenderer()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "renderer.json");
        var store = CreateStore(file);
        var snapshot = store.Load("fingerprint-a");
        snapshot.PreferredRenderer = CodexRendererCompatibilityStore.ClassicRendererId;
        store.Save(snapshot);

        var loaded = store.Load("fingerprint-b");

        Assert.Equal(CodexRendererCompatibilityStore.OfficialRendererId, loaded.PreferredRenderer);
        Assert.Equal("fingerprint-b", loaded.EnvironmentFingerprint);
    }

    [Fact]
    public void CorruptCacheIsPreservedAndFallsBackToOfficialRenderer()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "renderer.json");
        File.WriteAllText(file, "{not-json");
        var store = CreateStore(file);

        var snapshot = store.Load("fingerprint-a");

        Assert.Equal(CodexRendererCompatibilityStore.OfficialRendererId, snapshot.PreferredRenderer);
        Assert.False(string.IsNullOrWhiteSpace(store.LastLoadError));
        Assert.Single(Directory.GetFiles(temp.Path, "renderer-compatibility.corrupt.*.json"));
    }

    [Fact]
    public void FingerprintIncludesRendererPolicyAndAllCompatibilityInputs()
    {
        var fingerprint = CodexRendererEnvironmentFingerprint.Create(
            "18.7.3",
            "10.0.19045",
            "150.0.4078.65",
            "x64");

        Assert.Contains("policy=2", fingerprint, StringComparison.Ordinal);
        Assert.Contains("vs=18.7.3", fingerprint, StringComparison.Ordinal);
        Assert.Contains("os=10.0.19045", fingerprint, StringComparison.Ordinal);
        Assert.Contains("webview2=150.0.4078.65", fingerprint, StringComparison.Ordinal);
        Assert.Contains("arch=x64", fingerprint, StringComparison.Ordinal);
    }

    private static CodexRendererCompatibilityStore CreateStore(string file)
    {
        return new CodexRendererCompatibilityStore(
            file,
            "Local\\CodexVsix.RendererTests." + Guid.NewGuid().ToString("N"));
    }
}
