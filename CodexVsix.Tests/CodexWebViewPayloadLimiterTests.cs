using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexWebViewPayloadLimiterTests
{
    [Fact]
    public void LimitsOnlyTheVisualCopyAndPreservesEveryToolEvent()
    {
        var toolItems = new JArray();
        for (var index = 0; index < 24; index++)
        {
            toolItems.Add(new JObject
            {
                ["id"] = "tool-" + index,
                ["type"] = "commandExecution",
                ["status"] = "completed",
                ["aggregatedOutput"] = index == 0
                    ? new string('x', CodexWebViewPayloadLimiter.MaxToolOutputLength + 500)
                    : "ok"
            });
        }

        var original = new JObject
        {
            ["data"] = new JArray(
                new JObject
                {
                    ["id"] = "turn-1",
                    ["items"] = toolItems
                }),
            ["nextCursor"] = "older"
        };
        var limiter = new CodexWebViewPayloadLimiter();

        var limited = Assert.IsType<JObject>(limiter.LimitAppServerResult("thread/turns/list", original));
        var limitedItems = Assert.IsType<JArray>(limited["data"]![0]!["items"]);

        Assert.Equal(24, limitedItems.Count);
        Assert.Equal("tool-23", limitedItems[23]?["id"]?.Value<string>());
        var firstOutput = limitedItems[0]?["aggregatedOutput"]?.Value<string>();
        Assert.NotNull(firstOutput);
        Assert.Contains("Output truncated in Visual Studio", firstOutput!);
        Assert.Equal(
            CodexWebViewPayloadLimiter.MaxToolOutputLength + 500,
            original["data"]![0]!["items"]![0]!["aggregatedOutput"]!.Value<string>()!.Length);
    }

    [Fact]
    public void BoundsLegacyFullThreadReadsToTheRecentWindow()
    {
        var turns = new JArray();
        for (var index = 0; index < 150; index++)
        {
            turns.Add(new JObject { ["id"] = "turn-" + index });
        }

        var limiter = new CodexWebViewPayloadLimiter();
        var limited = Assert.IsType<JObject>(limiter.LimitAppServerResult(
            "thread/read",
            new JObject { ["thread"] = new JObject { ["turns"] = turns } }));
        var limitedTurns = Assert.IsType<JArray>(limited["thread"]?["turns"]);

        Assert.Equal(CodexWebViewHistoryWindowController.InitialTurnBudget, limitedTurns.Count);
        Assert.Equal("turn-30", limitedTurns[0]?["id"]?.Value<string>());
    }

    [Fact]
    public void StopsForwardingToolOutputAfterTheVisualStreamBudget()
    {
        var limiter = new CodexWebViewPayloadLimiter();
        var oversizedDelta = new JObject
        {
            ["threadId"] = "thread-1",
            ["turnId"] = "turn-1",
            ["itemId"] = "item-1",
            ["delta"] = new string('x', CodexWebViewPayloadLimiter.MaxToolStreamCharacters + 100)
        };

        Assert.True(limiter.TryLimitNotification(
            "item/commandExecution/outputDelta",
            oversizedDelta,
            out var first));
        Assert.Contains("Output truncated in Visual Studio", first?["delta"]?.Value<string>());

        Assert.False(limiter.TryLimitNotification(
            "item/commandExecution/outputDelta",
            new JObject
            {
                ["threadId"] = "thread-1",
                ["turnId"] = "turn-1",
                ["itemId"] = "item-1",
                ["delta"] = "more"
            },
            out var second));
        Assert.Null(second);
    }
}
