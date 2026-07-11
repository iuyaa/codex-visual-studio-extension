using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexToolWindowXamlTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void ReadOnlyTextBoxesUseOneWayTextBindings()
    {
        var document = XDocument.Load(FindRepositoryFile("CodexVsix", "CodexToolWindowControl.xaml"));
        var readOnlyBoundTextBoxes = document
            .Descendants(PresentationNamespace + "TextBox")
            .Where(IsReadOnlyTextBox)
            .Where(element => IsBinding(element.Attribute("Text")?.Value))
            .ToList();

        Assert.NotEmpty(readOnlyBoundTextBoxes);

        foreach (var textBox in readOnlyBoundTextBoxes)
        {
            var binding = textBox.Attribute("Text")!.Value;
            var name = textBox.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Name")
                ?.Value ?? "unnamed TextBox";

            Assert.True(
                binding.IndexOf("Mode=OneWay", StringComparison.OrdinalIgnoreCase) >= 0,
                $"The read-only {name} TextBox must use a OneWay Text binding, but found: {binding}");
        }
    }

    [Fact]
    public void ChatDisplayDetailBindingIsExplicitlyOneWay()
    {
        var document = XDocument.Load(FindRepositoryFile("CodexVsix", "CodexToolWindowControl.xaml"));
        var eventDetailText = document
            .Descendants(PresentationNamespace + "TextBox")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && string.Equals(attribute.Value, "EventDetailText", StringComparison.Ordinal)));

        Assert.Equal("{Binding DisplayDetail, Mode=OneWay}", eventDetailText.Attribute("Text")?.Value);
    }

    [Fact]
    public void SettingsOpenAsAnIndependentOfficialWebViewInTheDocumentWell()
    {
        var managerSource = File.ReadAllText(FindRepositoryFile("CodexVsix", "CodexToolWindowManager.cs"));
        var settingsWindowSource = File.ReadAllText(FindRepositoryFile("CodexVsix", "CodexSettingsToolWindow.cs"));

        Assert.Contains("VSFM_MdiChild", managerSource);
        Assert.Contains("VSFPROPID_FrameMode", managerSource);
        Assert.Contains("initialRoute: \"/settings\"", settingsWindowSource);
        Assert.Contains("isSettingsSurface: true", settingsWindowSource);
        Assert.Contains("registerAsPrimaryHost: false", settingsWindowSource);
        Assert.DoesNotContain("Content = new CodexSettingsToolWindowControl();", settingsWindowSource.Split(new[] { "OnWebViewFallbackRequested" }, StringSplitOptions.None)[0]);
    }

    private static bool IsReadOnlyTextBox(XElement element)
    {
        var isReadOnly = element.Attribute("IsReadOnly")?.Value;
        var style = element.Attribute("Style")?.Value;

        return string.Equals(isReadOnly, "True", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(style)
                && style!.IndexOf("ChatMessageTextBoxStyle", StringComparison.Ordinal) >= 0);
    }

    private static bool IsBinding(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value!.TrimStart().StartsWith("{Binding", StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var segments = new List<string> { current.FullName };
            segments.AddRange(relativePath);
            var candidate = Path.Combine(segments.ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate the repository file: " + Path.Combine(relativePath));
    }
}
