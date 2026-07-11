using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexWebViewPayloadLimiter
{
    internal const int MaxMessageTextLength = 60000;
    internal const int MaxToolOutputLength = 16000;
    internal const int MaxDiffLength = 40000;
    internal const int MaxAssistantStreamCharacters = 120000;
    internal const int MaxToolStreamCharacters = 60000;
    private const string OmittedMiddleMarker = "\n\n[...]\n\n";
    private const string TruncatedMarker = "\n\n[Output truncated in Visual Studio to keep the conversation responsive. The full content remains in Codex history.]";

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, int> _streamCharactersByItem = new(StringComparer.Ordinal);
    private readonly HashSet<string> _truncatedStreams = new(StringComparer.Ordinal);

    public JToken? LimitAppServerResult(string method, JToken? result)
    {
        if (result is null || !IsHistoryPayloadMethod(method))
        {
            return result;
        }

        var clone = result.DeepClone();

        if (!string.Equals(method, "thread/turns/list", StringComparison.Ordinal))
        {
            TrimNestedTurnCollections(clone);
        }

        SanitizeToken(clone, propertyName: null, inheritedToolContext: false);
        return clone;
    }

    public bool TryLimitNotification(string method, JToken? parameters, out JToken? limitedParameters)
    {
        limitedParameters = parameters?.DeepClone();
        if (limitedParameters is null)
        {
            return true;
        }

        var itemId = ReadIdentity(limitedParameters, "itemId", "item_id", "id");
        var threadId = ReadIdentity(limitedParameters, "threadId", "thread_id") ?? string.Empty;
        var turnId = ReadIdentity(limitedParameters, "turnId", "turn_id") ?? string.Empty;

        if (IsDeltaMethod(method)
            && TryFindStringProperty(limitedParameters, "delta", out var deltaProperty))
        {
            var isTool = IsToolMethod(method);
            var limit = isTool ? MaxToolStreamCharacters : MaxAssistantStreamCharacters;
            var key = threadId + "|" + turnId + "|" + (itemId ?? method) + "|" + GetStreamKind(method);
            var delta = deltaProperty.Value.Value<string>() ?? string.Empty;
            lock (_syncRoot)
            {
                if (_truncatedStreams.Contains(key))
                {
                    limitedParameters = null;
                    return false;
                }

                _streamCharactersByItem.TryGetValue(key, out var currentLength);
                var remaining = limit - currentLength;
                if (remaining <= 0)
                {
                    _truncatedStreams.Add(key);
                    limitedParameters = null;
                    return false;
                }

                if (delta.Length > remaining)
                {
                    deltaProperty.Value = delta.Substring(0, remaining).TrimEnd() + TruncatedMarker;
                    _streamCharactersByItem[key] = limit;
                    _truncatedStreams.Add(key);
                }
                else
                {
                    _streamCharactersByItem[key] = currentLength + delta.Length;
                }

                BoundStreamState();
            }

            return true;
        }

        if (method.EndsWith("/completed", StringComparison.OrdinalIgnoreCase)
            || method.EndsWith("/failed", StringComparison.OrdinalIgnoreCase)
            || method.EndsWith("/cancelled", StringComparison.OrdinalIgnoreCase))
        {
            ClearStreamState(threadId, turnId, itemId);
        }

        if (method.StartsWith("item/", StringComparison.OrdinalIgnoreCase)
            || method.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            SanitizeToken(limitedParameters, propertyName: null, inheritedToolContext: IsToolMethod(method));
        }

        return true;
    }

    internal static bool IsHistoryPayloadMethod(string method)
    {
        return string.Equals(method, "thread/turns/list", StringComparison.Ordinal)
            || string.Equals(method, "thread/read", StringComparison.Ordinal)
            || string.Equals(method, "thread/resume", StringComparison.Ordinal)
            || string.Equals(method, "thread/fork", StringComparison.Ordinal);
    }

    private static void TrimNestedTurnCollections(JToken token)
    {
        if (token is JObject record)
        {
            foreach (var property in record.Properties().ToList())
            {
                if (string.Equals(property.Name, "turns", StringComparison.OrdinalIgnoreCase)
                    && property.Value is JArray turns
                    && turns.Count > CodexWebViewHistoryWindowController.InitialTurnBudget)
                {
                    property.Value = new JArray(
                        turns.Skip(turns.Count - CodexWebViewHistoryWindowController.InitialTurnBudget)
                            .Select(turn => turn.DeepClone()));
                    continue;
                }

                TrimNestedTurnCollections(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array.ToList())
            {
                TrimNestedTurnCollections(child);
            }
        }
    }

    private static void SanitizeToken(JToken token, string? propertyName, bool inheritedToolContext)
    {
        switch (token)
        {
            case JObject record:
            {
                var type = record["type"]?.Value<string>() ?? string.Empty;
                var toolContext = inheritedToolContext || IsToolType(type);
                foreach (var property in record.Properties().ToList())
                {
                    SanitizeToken(property.Value, property.Name, toolContext);
                }

                break;
            }
            case JArray array:
                foreach (var child in array.ToList())
                {
                    SanitizeToken(child, propertyName, inheritedToolContext);
                }
                break;
            case JValue value when value.Type == JTokenType.String:
            {
                var text = value.Value<string>();
                var limit = GetStringLimit(propertyName, inheritedToolContext);
                if (text is not null && limit > 0 && text.Length > limit)
                {
                    value.Value = TruncateMiddle(text, limit);
                }

                break;
            }
        }
    }

    private static bool IsToolMethod(string method)
    {
        return method.IndexOf("commandExecution", StringComparison.OrdinalIgnoreCase) >= 0
            || method.IndexOf("toolCall", StringComparison.OrdinalIgnoreCase) >= 0
            || method.IndexOf("dynamicTool", StringComparison.OrdinalIgnoreCase) >= 0
            || method.IndexOf("fileChange", StringComparison.OrdinalIgnoreCase) >= 0
            || method.IndexOf("webSearch", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsToolType(string type)
    {
        return type.IndexOf("commandExecution", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("toolCall", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("dynamicTool", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("fileChange", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("webSearch", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int GetStringLimit(string? propertyName, bool toolContext)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return 0;
        }

        if (Matches(propertyName!, "diff", "patch", "unifiedDiff"))
        {
            return MaxDiffLength;
        }

        if (Matches(propertyName!, "output", "stdout", "stderr", "aggregatedOutput", "details"))
        {
            return MaxToolOutputLength;
        }

        if (Matches(propertyName!, "text", "content", "message", "summary", "delta"))
        {
            return toolContext ? MaxToolOutputLength : MaxMessageTextLength;
        }

        return 0;
    }

    private static bool Matches(string value, params string[] candidates)
    {
        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string TruncateMiddle(string value, int maxLength)
    {
        var suffix = TruncatedMarker;
        var contentBudget = Math.Max(0, maxLength - OmittedMiddleMarker.Length - suffix.Length);
        var headLength = contentBudget / 2;
        var tailLength = contentBudget - headLength;
        return value.Substring(0, headLength).TrimEnd()
            + OmittedMiddleMarker
            + value.Substring(value.Length - tailLength).TrimStart()
            + suffix;
    }

    private static bool TryFindStringProperty(JToken token, string name, out JProperty property)
    {
        if (token is JObject record)
        {
            foreach (var candidate in record.Properties())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                    && candidate.Value.Type == JTokenType.String)
                {
                    property = candidate;
                    return true;
                }

                if (TryFindStringProperty(candidate.Value, name, out property))
                {
                    return true;
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                if (TryFindStringProperty(child, name, out property))
                {
                    return true;
                }
            }
        }

        property = null!;
        return false;
    }

    private static string? ReadIdentity(JToken token, params string[] names)
    {
        if (token is JObject record)
        {
            foreach (var name in names)
            {
                var value = record[name]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            foreach (var property in record.Properties())
            {
                var nested = ReadIdentity(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string GetStreamKind(string method)
    {
        return IsDeltaMethod(method)
            ? method.Substring(0, method.Length - "Delta".Length)
            : method;
    }

    private static bool IsDeltaMethod(string method)
    {
        return method.EndsWith("Delta", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearStreamState(string threadId, string turnId, string? itemId)
    {
        lock (_syncRoot)
        {
            var prefix = threadId + "|" + turnId + "|";
            foreach (var key in _streamCharactersByItem.Keys
                         .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)
                             && (string.IsNullOrWhiteSpace(itemId) || key.IndexOf("|" + itemId + "|", StringComparison.Ordinal) >= 0))
                         .ToList())
            {
                _streamCharactersByItem.Remove(key);
                _truncatedStreams.Remove(key);
            }
        }
    }

    private void BoundStreamState()
    {
        if (_streamCharactersByItem.Count <= 512)
        {
            return;
        }

        _streamCharactersByItem.Clear();
        _truncatedStreams.Clear();
    }
}
