using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexWebViewAutoCompactionCoordinatorTests
{
    [Fact]
    public async Task CompactsAfterTheTurnCompletesWhenContextUsageCrossesTheThreshold()
    {
        var compacted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new CodexWebViewAutoCompactionCoordinator(
            () => true,
            (threadId, _) =>
            {
                compacted.TrySetResult(threadId);
                return Task.CompletedTask;
            },
            _ => { });

        coordinator.ObserveNotification(
            "thread/tokenUsage/updated",
            new JObject
            {
                ["threadId"] = "thread-compact",
                ["tokenUsage"] = new JObject
                {
                    ["last"] = new JObject { ["totalTokens"] = 90 },
                    ["modelContextWindow"] = 100
                }
            });
        coordinator.ObserveNotification(
            "turn/completed",
            new JObject { ["threadId"] = "thread-compact", ["turnId"] = "turn-1" });

        var completed = await Task.WhenAny(compacted.Task, Task.Delay(3000));
        Assert.Same(compacted.Task, completed);
        Assert.Equal("thread-compact", await compacted.Task);
    }

    [Fact]
    public void ReadsTheSupportedTokenUsageShape()
    {
        Assert.True(CodexWebViewAutoCompactionCoordinator.TryReadContextUsage(
            "thread/tokenUsage/updated",
            new JObject
            {
                ["threadId"] = "thread-1",
                ["tokenUsage"] = new JObject
                {
                    ["lastTokenUsage"] = new JObject { ["totalTokens"] = 850 },
                    ["modelContextWindow"] = 1000
                }
            },
            out var threadId,
            out var used,
            out var window));

        Assert.Equal("thread-1", threadId);
        Assert.Equal(850, used);
        Assert.Equal(1000, window);
    }
}
