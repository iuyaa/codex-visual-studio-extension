using System;
using System.IO;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexExecutableResolverTests
{
    [Fact]
    public void LegacyWindowsCodexCmdDefaultNormalizesToGenericCodexCommand()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        Assert.Equal("codex", CodexExecutableResolver.NormalizeConfiguredExecutablePath("codex.cmd"));
        Assert.Equal("codex", CodexExecutableResolver.NormalizeConfiguredExecutablePath("codex"));
    }

    [Fact]
    public void LegacyWindowsCodexCmdDefaultCanResolveDesktopCodexExeFromPath()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "codex-vsix-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = Path.Combine(directory, "codex.exe");
            File.WriteAllBytes(expected, Array.Empty<byte>());

            var actual = CodexExecutableResolver.ResolveExecutableLocation(
                "codex.cmd",
                "PATH=" + directory);

            Assert.Equal(expected, actual, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
