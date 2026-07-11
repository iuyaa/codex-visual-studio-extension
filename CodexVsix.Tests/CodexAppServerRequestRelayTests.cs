using System.Collections.Generic;
using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexAppServerRequestRelayTests
{
    [Fact]
    public async Task RelaysServerRequestAndMatchesOfficialWebviewResponse()
    {
        var posted = new List<JObject>();
        using var relay = new CodexAppServerRequestRelay(message => posted.Add(message));
        var pending = relay.ForwardAsync(new JObject
        {
            ["id"] = 42,
            ["method"] = "item/commandExecution/requestApproval",
            ["params"] = new JObject { ["threadId"] = "thread-1" }
        });

        var envelope = Assert.Single(posted);
        Assert.Equal("mcp-request", envelope["type"]?.Value<string>());
        Assert.Equal("local", envelope["hostId"]?.Value<string>());
        Assert.Equal(42, envelope["request"]?["id"]?.Value<int>());

        Assert.True(relay.TryHandleResponse(new JObject
        {
            ["type"] = "mcp-response",
            ["hostId"] = "local",
            ["response"] = new JObject
            {
                ["id"] = 42,
                ["result"] = new JObject { ["decision"] = "accept" }
            }
        }));

        var response = await pending;
        Assert.Equal("accept", response?["result"]?["decision"]?.Value<string>());
    }

    [Fact]
    public async Task CancelPendingReturnsControlToClassicFallback()
    {
        using var relay = new CodexAppServerRequestRelay(_ => { });
        var pending = relay.ForwardAsync(new JObject
        {
            ["id"] = "request-1",
            ["method"] = "item/tool/requestUserInput",
            ["params"] = new JObject()
        });

        relay.CancelPending();

        Assert.Null(await pending);
    }
}
