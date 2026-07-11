using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class SolutionContextServiceTests
{
    [Fact]
    public void FormatPathRequiresARealDirectoryBoundary()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "foo");
        var inside = Path.Combine(root, "src", "file.cs");
        var sibling = Path.Combine(temp.Path, "foobar", "file.cs");
        var service = new SolutionContextService();

        Assert.Equal("src/file.cs", service.FormatPathForPrompt(root, inside));
        Assert.Equal(Path.GetFullPath(sibling).Replace('\\', '/'), service.FormatPathForPrompt(root, sibling));
    }

    [Fact]
    public void FormatPathPreservesDriveRootContainment()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.GetPathRoot(temp.Path)!;
        var service = new SolutionContextService();

        var result = service.FormatPathForPrompt(root, temp.Path);

        Assert.False(Path.IsPathRooted(result));
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public async Task FileIndexPrunesGeneratedDirectoriesAndSupportsSpaces()
    {
        using var temp = new TemporaryDirectory();
        WriteFile(Path.Combine(temp.Path, "src", "Feature Folder", "Good File.cs"));
        WriteFile(Path.Combine(temp.Path, "bin", "Ignored.cs"));
        WriteFile(Path.Combine(temp.Path, "node_modules", "IgnoredToo.js"));
        var service = new SolutionContextService();

        var results = await service.FindSolutionFilesAsync(temp.Path, "Good File", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("src/Feature Folder/Good File.cs", results.Single());
        Assert.DoesNotContain(results, path => path.IndexOf("Ignored", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
    }
}
