using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

/// <summary>
/// Implements the GitHub CLI portion of the CyberVinci Codex host contract.
/// The official webview can therefore use its pull-request surfaces unchanged
/// when <c>gh</c> is installed and authenticated on the developer machine.
/// </summary>
internal sealed class CodexGitHubService
{
    public async Task<JToken?> HandleAsync(
        string method,
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "gh-cli-status":
                return await GetCliStatusAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-current-user":
                return await GetCurrentUserAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-status":
                return await GetPullRequestStatusAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-board":
                return await GetPullRequestBoardAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-body":
                return await GetPullRequestBodyAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-checks":
                return await GetPullRequestChecksAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-comments":
                return await GetPullRequestCommentsAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-diff":
                return await GetPullRequestDiffAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-create":
                return await CreatePullRequestAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-merge":
                return await MergePullRequestAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-update":
                return await UpdatePullRequestAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            case "gh-pr-comment":
                return await CommentOnPullRequestAsync(values, fallbackDirectory, cancellationToken).ConfigureAwait(false);
            default:
                throw new NotSupportedException("Unknown GitHub host method: " + method);
        }
    }

    private async Task<JObject> GetCliStatusAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var cwd = ResolveWorkingDirectory(values, fallbackDirectory);
        var version = await RunAsync(new[] { "--version" }, cwd, cancellationToken).ConfigureAwait(false);
        if (!version.Success)
        {
            return new JObject
            {
                ["isInstalled"] = false,
                ["isAuthenticated"] = false,
                ["stdout"] = version.Stdout,
                ["stderr"] = version.Stderr,
                ["error"] = ToNullableValue(version.Error)
            };
        }

        var auth = await RunAsync(new[] { "auth", "status" }, cwd, cancellationToken).ConfigureAwait(false);
        return new JObject
        {
            ["isInstalled"] = true,
            ["isAuthenticated"] = auth.Success,
            ["stdout"] = version.Stdout + auth.Stdout,
            ["stderr"] = auth.Stderr,
            ["error"] = auth.Success ? JValue.CreateNull() : ToNullableValue(auth.Error ?? NullIfEmpty(auth.Stderr))
        };
    }

    private async Task<JObject> GetCurrentUserAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            new[] { "api", "user", "--jq", ".login" },
            ResolveWorkingDirectory(values, fallbackDirectory),
            cancellationToken).ConfigureAwait(false);
        return new JObject
        {
            ["success"] = result.Success,
            ["login"] = result.Success ? ToNullableValue(NullIfEmpty(result.Stdout)) : JValue.CreateNull(),
            ["stdout"] = result.Stdout,
            ["stderr"] = result.Stderr,
            ["error"] = ToNullableValue(result.Error)
        };
    }

    private async Task<JObject> GetPullRequestStatusAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values) ?? ExtractString(values, "headBranch");
        if (selector is null)
        {
            return new JObject { ["status"] = "not-found" };
        }

        var repo = ExtractRepository(values);
        var result = await RunPullRequestViewAsync(
            selector,
            values,
            fallbackDirectory,
            repo,
            new[]
            {
                "number", "title", "url", "state", "isDraft", "mergeable", "headRefName",
                "baseRefName", "statusCheckRollup", "author", "repository"
            },
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new JObject
            {
                ["status"] = "not-found",
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
                ["error"] = ToNullableValue(result.Error)
            };
        }

        var data = ParseObject(result.Stdout);
        var item = ToPullRequestItem(data, values, repo, fallbackDirectory);
        var response = new JObject
        {
            ["status"] = "success",
            ["hasOpenPr"] = !string.Equals(data["state"]?.Value<string>(), "CLOSED", StringComparison.OrdinalIgnoreCase)
        };
        foreach (var property in item.Properties())
        {
            response[property.Name] = property.Value.DeepClone();
        }

        response["canMerge"] = string.Equals(data["mergeable"]?.Value<string>(), "MERGEABLE", StringComparison.Ordinal);
        response["mergeBlocker"] = string.Equals(data["mergeable"]?.Value<string>(), "CONFLICTING", StringComparison.Ordinal)
            ? "conflicts"
            : "unknown";
        response["commentAttachments"] = new JArray();
        response["activityItems"] = new JArray();
        response["boardItem"] = item.DeepClone();
        return response;
    }

    private async Task<JObject> GetPullRequestBoardAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var records = values["repos"] is JArray repositories
            ? repositories.OfType<JObject>().ToArray()
            : new[] { values };
        var items = new JArray();
        foreach (var record in records)
        {
            var arguments = new List<string>
            {
                "pr", "list", "--state", "all", "--limit", "100", "--json",
                "number,title,url,state,isDraft,mergeable,headRefName,baseRefName,statusCheckRollup,author,repository,updatedAt,createdAt"
            };
            AppendOptional(arguments, "--search", ExtractString(values, "searchQuery"));
            var repo = ExtractRepository(record);
            AppendOptional(arguments, "--repo", repo);
            var result = await RunAsync(
                arguments,
                ResolveWorkingDirectory(record, fallbackDirectory),
                cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                continue;
            }

            foreach (var entry in ParseArray(result.Stdout).OfType<JObject>())
            {
                items.Add(ToPullRequestItem(entry, record, repo, fallbackDirectory));
            }
        }

        return new JObject { ["status"] = "success", ["items"] = items };
    }

    private async Task<JToken> GetPullRequestBodyAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return JValue.CreateNull();
        }

        var result = await RunPullRequestViewAsync(
            selector,
            values,
            fallbackDirectory,
            ExtractRepository(values),
            new[] { "body" },
            cancellationToken).ConfigureAwait(false);
        return result.Success
            ? ToNullableValue(ParseObject(result.Stdout)["body"]?.Value<string>())
            : JValue.CreateNull();
    }

    private async Task<JObject> GetPullRequestChecksAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return new JObject { ["checks"] = new JArray(), ["ciStatus"] = "none" };
        }

        var result = await RunPullRequestViewAsync(
            selector,
            values,
            fallbackDirectory,
            ExtractRepository(values),
            new[] { "statusCheckRollup" },
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new JObject
            {
                ["checks"] = new JArray(),
                ["ciStatus"] = "none",
                ["error"] = ToNullableValue(result.Error ?? NullIfEmpty(result.Stderr))
            };
        }

        var checks = NormalizeChecks(ParseObject(result.Stdout)["statusCheckRollup"] as JArray);
        return new JObject { ["checks"] = checks, ["ciStatus"] = ComputeCiStatus(checks) };
    }

    private async Task<JObject> GetPullRequestCommentsAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return new JObject
            {
                ["repo"] = ToNullableValue(ExtractRepository(values)),
                ["activityItems"] = new JArray()
            };
        }

        var repo = ExtractRepository(values);
        var result = await RunPullRequestViewAsync(
            selector,
            values,
            fallbackDirectory,
            repo,
            new[] { "comments", "reviews", "repository" },
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new JObject
            {
                ["repo"] = ToNullableValue(repo),
                ["activityItems"] = new JArray(),
                ["error"] = ToNullableValue(result.Error ?? NullIfEmpty(result.Stderr))
            };
        }

        var data = ParseObject(result.Stdout);
        return new JObject
        {
            ["repo"] = ToNullableValue(repo ?? ExtractRepositoryName(data["repository"] as JObject)),
            ["activityItems"] = NormalizeActivityItems(data)
        };
    }

    private async Task<JObject> GetPullRequestDiffAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return new JObject { ["diff"] = string.Empty, ["unifiedDiff"] = string.Empty };
        }

        var arguments = new List<string> { "pr", "diff", selector };
        AppendOptional(arguments, "--repo", ExtractRepository(values));
        var result = await RunAsync(
            arguments,
            ResolveWorkingDirectory(values, fallbackDirectory),
            cancellationToken).ConfigureAwait(false);
        return BuildCommandResponse(result, new JObject
        {
            ["diff"] = result.Stdout,
            ["unifiedDiff"] = result.Stdout
        });
    }

    private async Task<JObject> CreatePullRequestAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var title = ExtractString(values, "title");
        if (title is null)
        {
            return new JObject { ["success"] = false, ["error"] = "Missing pull request title" };
        }

        var result = await RunWithBodyFileAsync(
            ExtractString(values, "body", "description") ?? string.Empty,
            async bodyPath =>
            {
                var arguments = new List<string> { "pr", "create", "--title", title, "--body-file", bodyPath };
                AppendOptional(arguments, "--base", ExtractString(values, "baseBranch", "base"));
                AppendOptional(arguments, "--head", ExtractString(values, "headBranch", "head"));
                if (values["draft"]?.Value<bool>() == true)
                {
                    arguments.Add("--draft");
                }
                AppendOptional(arguments, "--repo", ExtractRepository(values));
                return await RunAsync(arguments, ResolveWorkingDirectory(values, fallbackDirectory), cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
        return BuildCommandResponse(result, new JObject { ["url"] = ToNullableValue(NullIfEmpty(result.Stdout)) });
    }

    private async Task<JObject> MergePullRequestAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return new JObject { ["success"] = false, ["error"] = "Missing pull request selector" };
        }

        var method = ExtractString(values, "mergeMethod", "method") ?? "merge";
        var arguments = new List<string>
        {
            "pr", "merge", selector,
            string.Equals(method, "squash", StringComparison.OrdinalIgnoreCase)
                ? "--squash"
                : string.Equals(method, "rebase", StringComparison.OrdinalIgnoreCase) ? "--rebase" : "--merge"
        };
        if (values["deleteBranch"]?.Value<bool>() == true)
        {
            arguments.Add("--delete-branch");
        }
        if (values["auto"]?.Value<bool>() == true)
        {
            arguments.Add("--auto");
        }
        AppendOptional(arguments, "--repo", ExtractRepository(values));
        return BuildCommandResponse(await RunAsync(
            arguments,
            ResolveWorkingDirectory(values, fallbackDirectory),
            cancellationToken).ConfigureAwait(false));
    }

    private async Task<JObject> UpdatePullRequestAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        if (selector is null)
        {
            return new JObject { ["success"] = false, ["error"] = "Missing pull request selector" };
        }

        var body = ExtractString(values, "body", "description");
        var result = await RunWithBodyFileAsync(
            body ?? string.Empty,
            async bodyPath =>
            {
                var arguments = new List<string> { "pr", "edit", selector };
                AppendOptional(arguments, "--title", ExtractString(values, "title"));
                if (body is not null)
                {
                    arguments.Add("--body-file");
                    arguments.Add(bodyPath);
                }
                AppendOptional(arguments, "--base", ExtractString(values, "baseBranch", "base"));
                AppendOptional(arguments, "--repo", ExtractRepository(values));
                return await RunAsync(arguments, ResolveWorkingDirectory(values, fallbackDirectory), cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
        return BuildCommandResponse(result);
    }

    private async Task<JObject> CommentOnPullRequestAsync(
        JObject values,
        string fallbackDirectory,
        CancellationToken cancellationToken)
    {
        var selector = ExtractPullRequestSelector(values);
        var body = ExtractString(values, "body", "comment", "text");
        if (selector is null || body is null)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "Missing pull request selector or comment body"
            };
        }

        var result = await RunWithBodyFileAsync(
            body,
            async bodyPath =>
            {
                var arguments = new List<string> { "pr", "comment", selector, "--body-file", bodyPath };
                AppendOptional(arguments, "--repo", ExtractRepository(values));
                return await RunAsync(arguments, ResolveWorkingDirectory(values, fallbackDirectory), cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
        return BuildCommandResponse(result);
    }

    private async Task<CommandResult> RunPullRequestViewAsync(
        string selector,
        JObject values,
        string fallbackDirectory,
        string? repo,
        IEnumerable<string> fields,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "pr", "view", selector, "--json", string.Join(",", fields) };
        AppendOptional(arguments, "--repo", repo);
        return await RunAsync(
            arguments,
            ResolveWorkingDirectory(values, fallbackDirectory),
            cancellationToken).ConfigureAwait(false);
    }

    private static JObject ToPullRequestItem(
        JObject data,
        JObject values,
        string? fallbackRepo,
        string fallbackDirectory)
    {
        var checks = NormalizeChecks(data["statusCheckRollup"] as JArray);
        var ciStatus = ComputeCiStatus(checks);
        var isDraft = data["isDraft"]?.Value<bool>() == true;
        var rawState = data["state"]?.Value<string>()?.ToUpperInvariant() ?? "OPEN";
        var state = rawState == "MERGED"
            ? "merged"
            : isDraft
                ? "draft"
                : ciStatus == "failing" ? "failing" : ciStatus == "pending" ? "in_progress" : "ready";
        var author = data["author"] as JObject;
        return new JObject
        {
            ["cwd"] = ResolveWorkingDirectory(values, fallbackDirectory),
            ["hostId"] = ExtractString(values, "hostId") ?? "local",
            ["repo"] = ToNullableValue(fallbackRepo ?? ExtractRepositoryName(data["repository"] as JObject)),
            ["number"] = data["number"]?.DeepClone() ?? JValue.CreateNull(),
            ["title"] = data["title"]?.Value<string>() ?? string.Empty,
            ["url"] = ToNullableValue(data["url"]?.Value<string>()),
            ["state"] = state,
            ["isDraft"] = isDraft,
            ["isAuthor"] = false,
            ["authorLogin"] = ToNullableValue(author?["login"]?.Value<string>()),
            ["authorAvatarUrl"] = ToNullableValue(author?["avatarUrl"]?.Value<string>()),
            ["headBranch"] = data["headRefName"]?.Value<string>() ?? string.Empty,
            ["baseBranch"] = data["baseRefName"]?.Value<string>() ?? string.Empty,
            ["canMerge"] = string.Equals(data["mergeable"]?.Value<string>(), "MERGEABLE", StringComparison.Ordinal),
            ["mergeBlocker"] = string.Equals(data["mergeable"]?.Value<string>(), "CONFLICTING", StringComparison.Ordinal)
                ? "conflicts"
                : "unknown",
            ["ciStatus"] = ciStatus,
            ["checks"] = checks,
            ["commentAttachments"] = new JArray(),
            ["activityItems"] = new JArray(),
            ["updatedAt"] = ToNullableValue(data["updatedAt"]?.Value<string>()),
            ["createdAt"] = ToNullableValue(data["createdAt"]?.Value<string>())
        };
    }

    private static JArray NormalizeChecks(JArray? rawChecks)
    {
        var checks = new JArray();
        if (rawChecks is null)
        {
            return checks;
        }

        foreach (var entry in rawChecks.OfType<JObject>())
        {
            var conclusion = entry["conclusion"]?.Value<string>()?.ToUpperInvariant();
            var status = entry["status"]?.Value<string>()?.ToUpperInvariant();
            var normalized = conclusion == "SUCCESS"
                ? "passing"
                : conclusion == "SKIPPED"
                    ? "skipped"
                    : !string.IsNullOrWhiteSpace(conclusion)
                        ? "failing"
                        : !string.IsNullOrWhiteSpace(status) && status != "COMPLETED" ? "pending" : "unknown";
            checks.Add(new JObject
            {
                ["name"] = ExtractString(entry, "name", "workflowName") ?? "Check",
                ["status"] = normalized,
                ["link"] = ToNullableValue(ExtractString(entry, "detailsUrl", "url")),
                ["workflow"] = ToNullableValue(ExtractString(entry, "workflowName"))
            });
        }

        return checks;
    }

    private static string ComputeCiStatus(JArray checks)
    {
        if (checks.Count == 0)
        {
            return "none";
        }
        if (checks.OfType<JObject>().Any(check => check["status"]?.Value<string>() == "failing"))
        {
            return "failing";
        }
        if (checks.OfType<JObject>().Any(check => check["status"]?.Value<string>() == "pending"))
        {
            return "pending";
        }
        return "passing";
    }

    private static JArray NormalizeActivityItems(JObject data)
    {
        var items = new List<JObject>();
        foreach (var comment in (data["comments"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            var author = comment["author"] as JObject;
            var createdAt = comment["createdAt"]?.Value<string>() ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            items.Add(new JObject
            {
                ["type"] = "comment",
                ["id"] = comment["id"]?.Value<string>()
                    ?? (author?["login"]?.Value<string>() ?? "comment") + "-" + createdAt,
                ["authorLogin"] = ToNullableValue(author?["login"]?.Value<string>()),
                ["authorAvatarUrl"] = ToNullableValue(author?["avatarUrl"]?.Value<string>()),
                ["body"] = comment["body"]?.Value<string>() ?? string.Empty,
                ["createdAt"] = createdAt,
                ["url"] = ToNullableValue(comment["url"]?.Value<string>()),
                ["replies"] = new JArray()
            });
        }

        foreach (var review in (data["reviews"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            var state = review["state"]?.Value<string>();
            var normalizedEvent = state == "APPROVED"
                ? "approved"
                : state == "CHANGES_REQUESTED" ? "changes_requested" : null;
            if (normalizedEvent is null)
            {
                continue;
            }

            var author = review["author"] as JObject;
            var createdAt = review["submittedAt"]?.Value<string>() ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            items.Add(new JObject
            {
                ["type"] = "event",
                ["id"] = review["id"]?.Value<string>()
                    ?? (author?["login"]?.Value<string>() ?? "review") + "-" + createdAt,
                ["actorLogin"] = ToNullableValue(author?["login"]?.Value<string>()),
                ["event"] = normalizedEvent,
                ["createdAt"] = createdAt
            });
        }

        return new JArray(items.OrderBy(item => item["createdAt"]?.Value<string>(), StringComparer.Ordinal));
    }

    private static JObject BuildCommandResponse(CommandResult result, JObject? extra = null)
    {
        var response = new JObject
        {
            ["success"] = result.Success,
            ["stdout"] = result.Stdout,
            ["stderr"] = result.Stderr,
            ["error"] = ToNullableValue(result.Error)
        };
        if (extra is not null)
        {
            foreach (var property in extra.Properties())
            {
                response[property.Name] = property.Value.DeepClone();
            }
        }
        return response;
    }

    private static async Task<CommandResult> RunWithBodyFileAsync(
        string body,
        Func<string, Task<CommandResult>> run)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "codex-gh-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            return await run(path).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static async Task<CommandResult> RunAsync(
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return CommandResult.Failed("GitHub CLI could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exitTask = Task.Run(() => process.WaitForExit());
            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           if (!process.HasExited)
                           {
                               process.Kill();
                           }
                       }
                       catch
                       {
                       }
                   }))
            {
                await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new CommandResult(
                process.ExitCode == 0,
                stdout,
                stderr,
                process.ExitCode == 0 ? null : NullIfEmpty(stderr) ?? "GitHub CLI command failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CommandResult.Failed(ex.Message);
        }
    }

    private static string ResolveWorkingDirectory(JObject values, string fallbackDirectory)
    {
        var candidate = ExtractString(values, "cwd", "gitRoot", "root") ?? fallbackDirectory;
        return Directory.Exists(candidate)
            ? Path.GetFullPath(candidate)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string? ExtractPullRequestSelector(JObject values)
    {
        foreach (var key in new[] { "number", "prNumber", "pullRequestNumber" })
        {
            var token = values[key];
            if (token?.Type == JTokenType.Integer)
            {
                return token.Value<long>().ToString(CultureInfo.InvariantCulture);
            }
            if (token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                return token.Value<string>();
            }
        }

        var direct = ExtractString(values, "url", "prUrl", "pullRequestUrl", "selector");
        if (direct is not null)
        {
            return direct;
        }

        foreach (var key in new[] { "item", "pr", "pullRequest" })
        {
            if (values[key] is JObject nested)
            {
                var selector = ExtractPullRequestSelector(nested);
                if (selector is not null)
                {
                    return selector;
                }
            }
        }

        return null;
    }

    private static string? ExtractRepository(JObject values)
    {
        var direct = ExtractString(values, "repo");
        return direct ?? ExtractRepositoryName(values["repository"] as JObject);
    }

    private static string? ExtractRepositoryName(JObject? repository)
    {
        if (repository is null)
        {
            return null;
        }
        var withOwner = ExtractString(repository, "nameWithOwner");
        if (withOwner is not null)
        {
            return withOwner;
        }
        var owner = repository["owner"] as JObject;
        var ownerName = ExtractString(owner ?? repository, "login", "owner");
        var name = ExtractString(repository, "name", "repo", "repoName");
        return ownerName is not null && name is not null ? ownerName + "/" + name : null;
    }

    private static JObject ParseObject(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch
        {
            return new JObject();
        }
    }

    private static JArray ParseArray(string json)
    {
        try
        {
            return JArray.Parse(json);
        }
        catch
        {
            return new JArray();
        }
    }

    private static void AppendOptional(ICollection<string> arguments, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        arguments.Add(flag);
        arguments.Add(value!);
    }

    private static string? ExtractString(JObject values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = values[key]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character != '"'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
                continue;
            }
            builder.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static JToken ToNullableValue(string? value)
    {
        return value is null ? JValue.CreateNull() : new JValue(value);
    }

    private sealed class CommandResult
    {
        public CommandResult(bool success, string stdout, string stderr, string? error)
        {
            Success = success;
            Stdout = stdout;
            Stderr = stderr;
            Error = error;
        }

        public bool Success { get; }
        public string Stdout { get; }
        public string Stderr { get; }
        public string? Error { get; }

        public static CommandResult Failed(string error)
        {
            return new CommandResult(false, string.Empty, string.Empty, error);
        }
    }
}
