using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal static class CodexWebViewIdeContext
{
    private const int MaxOpenTabs = 20;

    public static JObject BuildResponse(
        string workspaceRoot,
        string? activeFilePath,
        string? activeSelectionContent,
        IEnumerable<string>? openDocumentPaths)
    {
        var ideContext = new JObject
        {
            ["workspaceRoot"] = workspaceRoot,
            ["workspaceFolders"] = new JArray(workspaceRoot),
            ["openTabs"] = BuildOpenTabs(activeFilePath, openDocumentPaths)
        };

        if (!string.IsNullOrWhiteSpace(activeFilePath))
        {
            var activeFile = new JObject
            {
                ["path"] = activeFilePath
            };
            if (!string.IsNullOrWhiteSpace(activeSelectionContent))
            {
                activeFile["activeSelectionContent"] = activeSelectionContent;
            }

            ideContext["activeFile"] = activeFile;
        }

        return new JObject { ["ideContext"] = ideContext };
    }

    private static JArray BuildOpenTabs(string? activeFilePath, IEnumerable<string>? openDocumentPaths)
    {
        var paths = Enumerable.Empty<string>();
        if (!string.IsNullOrWhiteSpace(activeFilePath))
        {
            paths = paths.Concat(new[] { activeFilePath! });
        }

        if (openDocumentPaths is not null)
        {
            paths = paths.Concat(openDocumentPaths);
        }

        var tabs = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxOpenTabs)
            .Select(path => new JObject
            {
                ["label"] = Path.GetFileName(path),
                ["path"] = path
            });
        return new JArray(tabs);
    }
}
