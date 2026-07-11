using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexGitServiceTests
{
    [Fact]
    public async Task ReturnsCyberVinciCompatibleOriginAndStableMetadata()
    {
        using var directory = new TemporaryDirectory();
        RunGit(directory.Path, "init");
        RunGit(directory.Path, "config user.email codex-vsix@example.invalid");
        RunGit(directory.Path, "config user.name CodexVsix");
        RunGit(directory.Path, "checkout -b main");
        File.WriteAllText(Path.Combine(directory.Path, "tracked.txt"), "tracked");
        RunGit(directory.Path, "add tracked.txt");
        RunGit(directory.Path, "commit -m initial");
        RunGit(directory.Path, "remote add origin https://example.invalid/owner/repository.git");

        var service = new CodexGitService();
        var origins = await service.GetOriginsAsync(
            new JObject { ["dirs"] = new JArray(directory.Path) },
            directory.Path,
            CancellationToken.None);

        var origin = Assert.Single((JArray)origins["origins"]!);
        Assert.Equal(Path.GetFullPath(directory.Path), origin["root"]?.Value<string>());
        Assert.Equal("https://example.invalid/owner/repository.git", origin["originUrl"]?.Value<string>());
        Assert.False(string.IsNullOrWhiteSpace(origins["homeDir"]?.Value<string>()));

        var metadata = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "stable-metadata",
            new JObject { ["cwd"] = directory.Path },
            directory.Path,
            CancellationToken.None));
        Assert.True(metadata["isRepo"]?.Value<bool>());
        Assert.Equal("main", metadata["branch"]?.Value<string>());
        Assert.Equal(0, metadata["aheadCount"]?.Value<int>());
        Assert.Equal(0, metadata["behindCount"]?.Value<int>());
    }

    [Fact]
    public async Task ReportsUntrackedFilesUsingWorkerEnvelopeValueShape()
    {
        using var directory = new TemporaryDirectory();
        RunGit(directory.Path, "init");
        File.WriteAllText(Path.Combine(directory.Path, "untracked.txt"), "untracked");

        var service = new CodexGitService();
        var status = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "status-summary",
            new JObject { ["cwd"] = directory.Path },
            directory.Path,
            CancellationToken.None));

        Assert.Equal("success", status["type"]?.Value<string>());
        Assert.Equal(1, status["untrackedCount"]?.Value<int>());
    }

    [Fact]
    public async Task PreparesCyberVinciCompatibleWorktreeSnapshot()
    {
        using var directory = new TemporaryDirectory();
        RunGit(directory.Path, "init");
        RunGit(directory.Path, "config user.email codex-vsix@example.invalid");
        RunGit(directory.Path, "config user.name CodexVsix");
        File.WriteAllText(Path.Combine(directory.Path, "tracked.txt"), "tracked");
        RunGit(directory.Path, "add tracked.txt");
        RunGit(directory.Path, "commit -m initial");

        var service = new CodexGitService();
        var snapshot = Assert.IsType<JObject>(await service.HandleHostRequestAsync(
            "prepare-worktree-snapshot",
            new JObject { ["cwd"] = directory.Path },
            directory.Path,
            CancellationToken.None));

        Assert.True(snapshot["success"]?.Value<bool>());
        Assert.Equal("application/gzip", snapshot["contentType"]?.Value<string>());
        var tarballPath = Assert.IsType<JValue>(snapshot["tarballPath"]).Value<string>();
        Assert.False(string.IsNullOrWhiteSpace(tarballPath));
        Assert.True(File.Exists(tarballPath));
        Assert.True(snapshot["size"]?.Value<long>() > 0);
        File.Delete(tarballPath!);

        var missingUpload = Assert.IsType<JObject>(await service.HandleHostRequestAsync(
            "upload-worktree-snapshot",
            new JObject(),
            directory.Path,
            CancellationToken.None));
        Assert.False(missingUpload["success"]?.Value<bool>());
    }

    [Fact]
    public async Task ImplementsCyberVinciBranchHistoryAndFileInspectionMethods()
    {
        using var directory = CreateRepositoryWithFeatureCommit();
        var service = new CodexGitService();
        var values = new JObject { ["cwd"] = directory.Path };

        var branchCommits = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "branch-commits",
            new JObject
            {
                ["cwd"] = directory.Path,
                ["baseBranch"] = "main",
                ["branch"] = "feature/review"
            },
            directory.Path,
            CancellationToken.None));
        var commit = Assert.Single((JArray)branchCommits["commits"]!);
        Assert.Equal("feature commit", commit["subject"]?.Value<string>());

        var search = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "search-branches",
            new JObject { ["cwd"] = directory.Path, ["query"] = "review" },
            directory.Path,
            CancellationToken.None));
        Assert.Contains("feature/review", ((JArray)search["branches"]!).Values<string>());

        var nearest = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "nearest-ancestor-branch",
            new JObject { ["cwd"] = directory.Path, ["candidates"] = new JArray("main") },
            directory.Path,
            CancellationToken.None));
        Assert.Equal("main", nearest["branch"]?.Value<string>());

        var stats = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "branch-diff-stats",
            new JObject { ["cwd"] = directory.Path, ["baseBranch"] = "main" },
            directory.Path,
            CancellationToken.None));
        Assert.Equal(1, stats["fileCount"]?.Value<int>());
        Assert.Equal(1, stats["additions"]?.Value<int>());

        var catFile = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "cat-file",
            new JObject { ["cwd"] = directory.Path, ["oid"] = "HEAD", ["path"] = "tracked.txt" },
            directory.Path,
            CancellationToken.None));
        Assert.Equal("success", catFile["type"]?.Value<string>());
        Assert.Contains("feature line", catFile["contents"]?.Value<string>());

        var blame = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "blame-file",
            new JObject { ["cwd"] = directory.Path, ["path"] = "tracked.txt" },
            directory.Path,
            CancellationToken.None));
        Assert.Equal("success", blame["type"]?.Value<string>());
        Assert.Equal(2, ((JArray)blame["lines"]!).Count);

        var setConfig = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "set-config-value",
            new JObject { ["cwd"] = directory.Path, ["key"] = "codex.test", ["value"] = "enabled" },
            directory.Path,
            CancellationToken.None));
        Assert.True(setConfig["success"]?.Value<bool>());

        var config = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "config-value",
            new JObject { ["cwd"] = directory.Path, ["key"] = "codex.test" },
            directory.Path,
            CancellationToken.None));
        Assert.Equal("enabled", config["value"]?.Value<string>());
    }

    [Fact]
    public async Task ImplementsCyberVinciReviewAndCommitDiffMethods()
    {
        using var directory = CreateRepositoryWithFeatureCommit();
        File.AppendAllText(Path.Combine(directory.Path, "tracked.txt"), "working line\n");
        var service = new CodexGitService();
        var values = new JObject { ["cwd"] = directory.Path, ["source"] = "unstaged" };

        var summary = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "review-summary",
            values,
            directory.Path,
            CancellationToken.None));
        Assert.Equal("success", summary["type"]?.Value<string>());
        var file = Assert.Single((JArray)summary["files"]!);
        Assert.Equal("tracked.txt", file["path"]?.Value<string>());
        Assert.Equal(1, file["additions"]?.Value<int>());
        Assert.Equal(1, summary["stageCounts"]?["unstagedFileCount"]?.Value<int>());

        var reviewDiff = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "review-diff",
            new JObject
            {
                ["cwd"] = directory.Path,
                ["source"] = "unstaged",
                ["files"] = new JArray(new JObject { ["path"] = "tracked.txt" })
            },
            directory.Path,
            CancellationToken.None));
        Assert.Contains("working line", reviewDiff["diffs"]?["tracked.txt"]?["diff"]?.Value<string>());

        var search = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "review-search",
            new JObject { ["cwd"] = directory.Path, ["source"] = "unstaged", ["query"] = "working line" },
            directory.Path,
            CancellationToken.None));
        Assert.Equal(1, search["totalMatches"]?.Value<int>());

        var patch = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "review-patch",
            values,
            directory.Path,
            CancellationToken.None));
        Assert.Equal("success", patch["diff"]?["type"]?.Value<string>());
        Assert.Contains("working line", patch["diff"]?["unifiedDiff"]?.Value<string>());

        var commitDiff = Assert.IsType<JObject>(await service.HandleWorkerRequestAsync(
            "commit-message-diff",
            new JObject { ["cwd"] = directory.Path, ["includeUnstaged"] = true },
            directory.Path,
            CancellationToken.None));
        Assert.Equal("success", commitDiff["type"]?.Value<string>());
        Assert.Contains("working line", commitDiff["unifiedDiff"]?.Value<string>());
    }

    private static TemporaryDirectory CreateRepositoryWithFeatureCommit()
    {
        var directory = new TemporaryDirectory();
        RunGit(directory.Path, "init");
        RunGit(directory.Path, "config user.email codex-vsix@example.invalid");
        RunGit(directory.Path, "config user.name CodexVsix");
        RunGit(directory.Path, "checkout -b main");
        File.WriteAllText(Path.Combine(directory.Path, "tracked.txt"), "initial line\n");
        RunGit(directory.Path, "add tracked.txt");
        RunGit(directory.Path, "commit -m initial");
        RunGit(directory.Path, "checkout -b feature/review");
        File.AppendAllText(Path.Combine(directory.Path, "tracked.txt"), "feature line\n");
        RunGit(directory.Path, "add tracked.txt");
        RunGit(directory.Path, "commit -m \"feature commit\"");
        return directory;
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "-C \"" + workingDirectory + "\" " + arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + stderr);
    }
}
