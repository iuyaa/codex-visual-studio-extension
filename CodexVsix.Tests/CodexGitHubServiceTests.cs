using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexGitHubServiceTests
{
    [Fact]
    public async Task ReturnsCyberVinciNeutralShapesWithoutInvokingGhWhenSelectorIsMissing()
    {
        var service = new CodexGitHubService();
        var values = new JObject();
        var cwd = Path.GetTempPath();

        var status = Assert.IsType<JObject>(await service.HandleAsync(
            "gh-pr-status", values, cwd, CancellationToken.None));
        Assert.Equal("not-found", status["status"]?.Value<string>());

        var checks = Assert.IsType<JObject>(await service.HandleAsync(
            "gh-pr-checks", values, cwd, CancellationToken.None));
        Assert.Empty(Assert.IsType<JArray>(checks["checks"]));
        Assert.Equal("none", checks["ciStatus"]?.Value<string>());

        Assert.IsType<JValue>(await service.HandleAsync(
            "gh-pr-body", values, cwd, CancellationToken.None));

        var create = Assert.IsType<JObject>(await service.HandleAsync(
            "gh-pr-create", values, cwd, CancellationToken.None));
        Assert.False(create["success"]?.Value<bool>());

        var comment = Assert.IsType<JObject>(await service.HandleAsync(
            "gh-pr-comment", values, cwd, CancellationToken.None));
        Assert.False(comment["success"]?.Value<bool>());
    }
}
