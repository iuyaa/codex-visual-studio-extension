using System;
using System.Collections.Generic;
using System.Linq;
using CodexVsix.Models;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal static class CodexModelCatalog
{
    private static readonly string[] AllReasoningEfforts =
    {
        "minimal",
        "low",
        "medium",
        "high",
        "xhigh",
        "max",
        "ultra"
    };

    internal static IReadOnlyList<string> GenericReasoningEfforts => AllReasoningEfforts;

    internal static IReadOnlyList<CodexModelOption> ParseModelListResponse(JToken? response, bool includeHidden)
    {
        var models = new List<CodexModelOption>();
        if (response?["data"] is not JArray items)
        {
            return models;
        }

        foreach (var item in items)
        {
            if (!includeHidden && item?["hidden"]?.Value<bool>() == true)
            {
                continue;
            }

            var value = item?["model"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var label = item?["displayName"]?.Value<string>()?.Trim();
            var defaultReasoningEffort = NormalizeReasoningEffort(item?["defaultReasoningEffort"]?.Value<string>());
            var supportedReasoningEfforts = (item?["supportedReasoningEfforts"] as JArray)?
                .Select(ReadReasoningEffort)
                .Where(effort => !string.IsNullOrWhiteSpace(effort))
                .Select(effort => effort!)
                ?? Enumerable.Empty<string>();

            models.Add(new CodexModelOption(
                string.IsNullOrWhiteSpace(label) ? value! : label!,
                value!,
                defaultReasoningEffort,
                supportedReasoningEfforts));
        }

        return models;
    }

    internal static IReadOnlyList<CodexModelOption> CreateFallbackOptions()
    {
        return new[]
        {
            Create("GPT-5.6 Sol", "gpt-5.6-sol", "low", "low", "medium", "high", "xhigh", "max", "ultra"),
            Create("GPT-5.6 Terra", "gpt-5.6-terra", "medium", "low", "medium", "high", "xhigh", "max", "ultra"),
            Create("GPT-5.6 Luna", "gpt-5.6-luna", "medium", "low", "medium", "high", "xhigh", "max"),
            Create("GPT-5.5", "gpt-5.5", "medium", "low", "medium", "high", "xhigh"),
            Create("GPT-5.4", "gpt-5.4", "medium", "low", "medium", "high", "xhigh"),
            Create("GPT-5.4 Mini", "gpt-5.4-mini", "medium", "low", "medium", "high", "xhigh"),
            Create("GPT-5.2", "gpt-5.2", "medium", "low", "medium", "high", "xhigh")
        };
    }

    internal static string NormalizeReasoningEffort(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "minimum":
            case "min":
                return "minimal";
            case "extra-high":
            case "extra high":
            case "x-high":
                return "xhigh";
            case "maximum":
                return "max";
            default:
                return normalized;
        }
    }

    internal static string ResolveReasoningEffort(
        CodexModelOption? model,
        string? requestedEffort,
        string fallbackEffort = "high")
    {
        var requested = NormalizeReasoningEffort(requestedEffort);
        if (model is null || !model.HasReasoningEffortMetadata)
        {
            return string.IsNullOrWhiteSpace(requested)
                ? NormalizeReasoningEffort(fallbackEffort)
                : requested;
        }

        var supported = model.SupportedReasoningEfforts;
        var match = FindSupportedEffort(supported, requested);
        if (!string.IsNullOrWhiteSpace(match))
        {
            return match!;
        }

        match = FindSupportedEffort(supported, model.DefaultReasoningEffort);
        if (!string.IsNullOrWhiteSpace(match))
        {
            return match!;
        }

        match = FindSupportedEffort(supported, NormalizeReasoningEffort(fallbackEffort));
        return !string.IsNullOrWhiteSpace(match) ? match! : supported[0];
    }

    private static CodexModelOption Create(
        string label,
        string value,
        string defaultReasoningEffort,
        params string[] supportedReasoningEfforts)
    {
        return new CodexModelOption(label, value, defaultReasoningEffort, supportedReasoningEfforts);
    }

    private static string? ReadReasoningEffort(JToken? option)
    {
        if (option?.Type == JTokenType.String)
        {
            return NormalizeReasoningEffort(option.Value<string>());
        }

        return option is JObject objectOption
            ? NormalizeReasoningEffort(objectOption["reasoningEffort"]?.Value<string>())
            : null;
    }

    private static string? FindSupportedEffort(IEnumerable<string> supportedEfforts, string? effort)
    {
        var normalized = NormalizeReasoningEffort(effort);
        return supportedEfforts.FirstOrDefault(candidate =>
            string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
