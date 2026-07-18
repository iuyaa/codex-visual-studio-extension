using System;
using System.IO;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexDiagnosticLoggerTests
{
    [Fact]
    public void DisabledDiagnosticsDoNotCreateFiles()
    {
        using var temp = new TemporaryDirectory();
        var logDirectory = Path.Combine(temp.Path, "logs");
        var logger = new CodexDiagnosticLogger(logDirectory);

        logger.Write("renderer.test", new JObject { ["value"] = "ignored" });

        Assert.False(Directory.Exists(logDirectory));
    }

    [Fact]
    public void EnabledDiagnosticsWriteJsonLinesAndRedactSecrets()
    {
        using var temp = new TemporaryDirectory();
        var logger = new CodexDiagnosticLogger(Path.Combine(temp.Path, "logs"));
        logger.SetEnabled(true, writeTransition: false);

        logger.Write(
            "renderer.test",
            new JObject
            {
                ["authorization"] = "Bearer top-secret-token",
                ["key"] = "sk-abcdefghijklmnopqrstuvwxyz",
                ["assignment"] = "OPENAI_API_KEY=plain-secret-value"
            });

        var text = File.ReadAllText(logger.LogFilePath);
        var entry = JObject.Parse(text.Trim());
        Assert.Equal("renderer.test", entry["event"]?.Value<string>());
        Assert.Contains("Bearer [redacted]", text, StringComparison.Ordinal);
        Assert.Contains("[redacted-secret]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", text, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-secret-value", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsStopImmediatelyWhenDisabledAndRotateWithinBounds()
    {
        using var temp = new TemporaryDirectory();
        var logDirectory = Path.Combine(temp.Path, "logs");
        var logger = new CodexDiagnosticLogger(logDirectory, maximumFileBytes: 512, archiveCount: 2);
        logger.SetEnabled(true, writeTransition: false);

        for (var index = 0; index < 20; index++)
        {
            logger.Write(
                "rotation.test",
                new JObject { ["payload"] = new string('x', 180), ["index"] = index });
        }

        Assert.True(File.Exists(logger.LogFilePath));
        Assert.True(File.Exists(Path.Combine(logDirectory, "codex-diagnostics.1.jsonl")));
        Assert.True(Directory.GetFiles(logDirectory, "codex-diagnostics.*.jsonl").Length <= 2);

        logger.SetEnabled(false);
        var lengthAfterDisable = new FileInfo(logger.LogFilePath).Length;
        logger.Write("must.not.write", new JObject { ["value"] = 1 });
        Assert.Equal(lengthAfterDisable, new FileInfo(logger.LogFilePath).Length);
    }
}
