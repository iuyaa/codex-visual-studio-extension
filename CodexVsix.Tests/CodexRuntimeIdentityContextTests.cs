using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexRuntimeIdentityContextTests
{
    [Fact]
    public void ThreadStartPreservesDeveloperInstructionsAndReportsRequestedModel()
    {
        var request = new JObject
        {
            ["model"] = "gpt-5.6-sol",
            ["developerInstructions"] = "Keep existing project conventions."
        };

        var enriched = Assert.IsType<JObject>(CodexRuntimeIdentityContext.EnrichRequest(
            "thread/start",
            request,
            "fallback-model",
            "medium"));

        var instructions = enriched["developerInstructions"]?.Value<string>();
        Assert.Contains("Keep existing project conventions.", instructions);
        Assert.Contains("gpt-5.6-sol", instructions);
        Assert.Contains("medium", instructions);
        Assert.Equal("Keep existing project conventions.", request["developerInstructions"]?.Value<string>());
    }

    [Fact]
    public void TurnStartUsesCollaborationModelWithoutChangingVisibleInput()
    {
        var request = new JObject
        {
            ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = "Which model are you using?" }),
            ["collaborationMode"] = new JObject
            {
                ["mode"] = "default",
                ["settings"] = new JObject
                {
                    ["model"] = "gpt-5.6-sol",
                    ["reasoning_effort"] = "high",
                    ["developer_instructions"] = "Existing turn rule."
                }
            }
        };

        var enriched = Assert.IsType<JObject>(CodexRuntimeIdentityContext.EnrichRequest(
            "turn/start",
            request,
            null,
            null));

        Assert.Equal("Which model are you using?", enriched["input"]?[0]?["text"]?.Value<string>());
        var instructions = enriched["collaborationMode"]?["settings"]?["developer_instructions"]?.Value<string>();
        Assert.Contains("Existing turn rule.", instructions);
        Assert.Contains("gpt-5.6-sol", instructions);
        Assert.Contains("high", instructions);
    }

    [Fact]
    public void EnrichmentReplacesStaleRuntimeMetadataWhenTheModelChanges()
    {
        var first = new JObject
        {
            ["model"] = "gpt-5.6-sol"
        };
        var enrichedFirst = CodexRuntimeIdentityContext.EnrichRequest(
            "thread/start",
            first,
            null,
            "medium");
        var second = Assert.IsType<JObject>(enrichedFirst);
        second["model"] = "gpt-5.5-codex";

        var enrichedSecond = Assert.IsType<JObject>(CodexRuntimeIdentityContext.EnrichRequest(
            "thread/start",
            second,
            null,
            "high"));

        var instructions = enrichedSecond["developerInstructions"]?.Value<string>();
        Assert.DoesNotContain("gpt-5.6-sol", instructions);
        Assert.Contains("gpt-5.5-codex", instructions);
        Assert.Equal(1, CountOccurrences(instructions!, "[[visual-codex-studio-runtime-metadata:v1]]"));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
