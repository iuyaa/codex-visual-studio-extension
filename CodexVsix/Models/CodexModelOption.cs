using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexVsix.Models;

public sealed class CodexModelOption : SelectionOption
{
    public CodexModelOption(
        string label,
        string value,
        string? defaultReasoningEffort = null,
        IEnumerable<string>? supportedReasoningEfforts = null)
        : base(label, value)
    {
        DefaultReasoningEffort = Normalize(defaultReasoningEffort);
        SupportedReasoningEfforts = supportedReasoningEfforts?
            .Select(Normalize)
            .Where(effort => !string.IsNullOrWhiteSpace(effort))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    public string DefaultReasoningEffort { get; }

    public IReadOnlyList<string> SupportedReasoningEfforts { get; }

    public bool HasReasoningEffortMetadata => SupportedReasoningEfforts.Count > 0;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
