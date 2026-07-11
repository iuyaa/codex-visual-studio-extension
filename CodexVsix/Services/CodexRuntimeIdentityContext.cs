using System;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal static class CodexRuntimeIdentityContext
{
    private const string StartMarker = "[[visual-codex-studio-runtime-metadata:v1]]";
    private const string EndMarker = "[[/visual-codex-studio-runtime-metadata]]";

    public static JToken? EnrichRequest(
        string method,
        JToken? parameters,
        string? fallbackModel,
        string? fallbackReasoningEffort)
    {
        if (parameters is not JObject source)
        {
            return parameters?.DeepClone();
        }

        var enriched = (JObject)source.DeepClone();
        var model = ResolveModel(enriched, fallbackModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            return enriched;
        }

        var reasoningEffort = ResolveReasoningEffort(enriched, fallbackReasoningEffort);
        var runtimeInstructions = BuildRuntimeInstructions(model!, reasoningEffort);
        if (SupportsThreadDeveloperInstructions(method))
        {
            enriched["developerInstructions"] = UpsertRuntimeInstructions(
                enriched["developerInstructions"]?.Value<string>(),
                runtimeInstructions);
        }
        else if (string.Equals(method, "turn/start", StringComparison.Ordinal)
            && enriched["collaborationMode"]?["settings"] is JObject modeSettings)
        {
            modeSettings["developer_instructions"] = UpsertRuntimeInstructions(
                modeSettings["developer_instructions"]?.Value<string>(),
                runtimeInstructions);
        }

        return enriched;
    }

    private static string? ResolveModel(JObject parameters, string? fallbackModel)
    {
        return NormalizeMetadata(parameters["model"]?.Value<string>())
            ?? NormalizeMetadata(parameters["collaborationMode"]?["settings"]?["model"]?.Value<string>())
            ?? NormalizeMetadata(fallbackModel);
    }

    private static string? ResolveReasoningEffort(JObject parameters, string? fallbackReasoningEffort)
    {
        return NormalizeMetadata(parameters["effort"]?.Value<string>())
            ?? NormalizeMetadata(parameters["reasoningEffort"]?.Value<string>())
            ?? NormalizeMetadata(parameters["collaborationMode"]?["settings"]?["reasoning_effort"]?.Value<string>())
            ?? NormalizeMetadata(fallbackReasoningEffort);
    }

    private static bool SupportsThreadDeveloperInstructions(string method)
    {
        return string.Equals(method, "thread/start", StringComparison.Ordinal)
            || string.Equals(method, "thread/resume", StringComparison.Ordinal)
            || string.Equals(method, "thread/fork", StringComparison.Ordinal);
    }

    private static string BuildRuntimeInstructions(string model, string? reasoningEffort)
    {
        var effortText = string.IsNullOrWhiteSpace(reasoningEffort)
            ? string.Empty
            : $" The requested reasoning effort is \"{reasoningEffort}\".";
        return StartMarker + Environment.NewLine
            + $"The Visual Studio host reports that the model requested for this turn is \"{model}\".{effortText} "
            + "When asked which model is in use, report this exact host-provided model identifier instead of answering only \"Codex\"."
            + Environment.NewLine
            + EndMarker;
    }

    private static string UpsertRuntimeInstructions(string? existing, string runtimeInstructions)
    {
        var preserved = RemoveRuntimeInstructions(existing).Trim();
        return string.IsNullOrWhiteSpace(preserved)
            ? runtimeInstructions
            : preserved + Environment.NewLine + Environment.NewLine + runtimeInstructions;
    }

    private static string RemoveRuntimeInstructions(string? value)
    {
        var result = value ?? string.Empty;
        while (true)
        {
            var start = result.IndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return result;
            }

            var end = result.IndexOf(EndMarker, start + StartMarker.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                return result.Substring(0, start);
            }

            result = result.Remove(start, end + EndMarker.Length - start);
        }
    }

    private static string? NormalizeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value!.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 160 ? normalized : normalized.Substring(0, 160);
    }
}
