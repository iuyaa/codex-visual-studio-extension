using System.Linq;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexModelCatalogTests
{
    [Fact]
    public void ModelListResponsePreservesReasoningMetadataForEachModel()
    {
        var response = JObject.Parse(@"
        {
          ""data"": [
            {
              ""model"": ""gpt-5.6-sol"",
              ""displayName"": ""GPT-5.6-Sol"",
              ""defaultReasoningEffort"": ""low"",
              ""supportedReasoningEfforts"": [
                { ""reasoningEffort"": ""low"", ""description"": """" },
                { ""reasoningEffort"": ""medium"", ""description"": """" },
                { ""reasoningEffort"": ""high"", ""description"": """" },
                { ""reasoningEffort"": ""xhigh"", ""description"": """" },
                { ""reasoningEffort"": ""max"", ""description"": """" },
                { ""reasoningEffort"": ""ultra"", ""description"": """" }
              ],
              ""hidden"": false
            },
            {
              ""model"": ""gpt-5.6-luna"",
              ""displayName"": ""GPT-5.6-Luna"",
              ""defaultReasoningEffort"": ""medium"",
              ""supportedReasoningEfforts"": [
                { ""reasoningEffort"": ""low"", ""description"": """" },
                { ""reasoningEffort"": ""medium"", ""description"": """" },
                { ""reasoningEffort"": ""high"", ""description"": """" },
                { ""reasoningEffort"": ""xhigh"", ""description"": """" },
                { ""reasoningEffort"": ""max"", ""description"": """" }
              ],
              ""hidden"": false
            }
          ]
        }");

        var models = CodexModelCatalog.ParseModelListResponse(response, includeHidden: false);

        var sol = Assert.Single(models, model => model.Value == "gpt-5.6-sol");
        Assert.Equal("low", sol.DefaultReasoningEffort);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, sol.SupportedReasoningEfforts);

        var luna = Assert.Single(models, model => model.Value == "gpt-5.6-luna");
        Assert.Equal("medium", luna.DefaultReasoningEffort);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, luna.SupportedReasoningEfforts);
        Assert.DoesNotContain("ultra", luna.SupportedReasoningEfforts);
    }

    [Fact]
    public void HiddenModelsAreOnlyReturnedWhenRequested()
    {
        var response = JObject.Parse(@"
        {
          ""data"": [
            {
              ""model"": ""hidden-model"",
              ""displayName"": ""Hidden"",
              ""defaultReasoningEffort"": ""medium"",
              ""supportedReasoningEfforts"": [{ ""reasoningEffort"": ""medium"", ""description"": """" }],
              ""hidden"": true
            }
          ]
        }");

        Assert.Empty(CodexModelCatalog.ParseModelListResponse(response, includeHidden: false));
        Assert.Single(CodexModelCatalog.ParseModelListResponse(response, includeHidden: true));
    }

    [Fact]
    public void FallbackCatalogMatchesTheInstalledCodexReasoningMatrix()
    {
        var models = CodexModelCatalog.CreateFallbackOptions();

        var sol = Assert.Single(models, model => model.Value == "gpt-5.6-sol");
        var terra = Assert.Single(models, model => model.Value == "gpt-5.6-terra");
        var luna = Assert.Single(models, model => model.Value == "gpt-5.6-luna");

        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, sol.SupportedReasoningEfforts);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, terra.SupportedReasoningEfforts);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, luna.SupportedReasoningEfforts);
    }

    [Theory]
    [InlineData("maximum", "max")]
    [InlineData("max", "max")]
    [InlineData("extra high", "xhigh")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("ultra", "ultra")]
    public void ReasoningNormalizationKeepsNewEffortsDistinct(string input, string expected)
    {
        Assert.Equal(expected, CodexModelCatalog.NormalizeReasoningEffort(input));
    }

    [Fact]
    public void UnsupportedEffortFallsBackToTheSelectedModelsDefault()
    {
        var models = CodexModelCatalog.CreateFallbackOptions();
        var sol = models.Single(model => model.Value == "gpt-5.6-sol");
        var luna = models.Single(model => model.Value == "gpt-5.6-luna");

        Assert.Equal("ultra", CodexModelCatalog.ResolveReasoningEffort(sol, "ultra"));
        Assert.Equal("medium", CodexModelCatalog.ResolveReasoningEffort(luna, "ultra"));
        Assert.Equal("max", CodexModelCatalog.ResolveReasoningEffort(luna, "maximum"));
    }
}
