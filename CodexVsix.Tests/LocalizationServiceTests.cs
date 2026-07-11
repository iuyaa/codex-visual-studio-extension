using System.Globalization;
using System.Linq;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void ExtensionDefaultOverrideDoesNotChangeTheVisualStudioProcessCulture()
    {
        var currentCulture = CultureInfo.CurrentCulture.Name;
        var currentUiCulture = CultureInfo.CurrentUICulture.Name;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture?.Name;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture?.Name;

        LocalizationService.SetDefaultLanguageOverride("fr-FR");
        try
        {
            Assert.Equal("fr", new LocalizationService().LanguageTag);
            Assert.Equal(currentCulture, CultureInfo.CurrentCulture.Name);
            Assert.Equal(currentUiCulture, CultureInfo.CurrentUICulture.Name);
            Assert.Equal(defaultCulture, CultureInfo.DefaultThreadCurrentCulture?.Name);
            Assert.Equal(defaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture?.Name);
        }
        finally
        {
            LocalizationService.SetDefaultLanguageOverride(string.Empty);
        }
    }

    [Theory]
    [InlineData("en-US", "active editor")]
    [InlineData("pt-BR", "editor ativo")]
    [InlineData("es-ES", "editor activo")]
    [InlineData("fr-FR", "éditeur actif")]
    [InlineData("de-DE", "aktiver Editor")]
    public void IdeCommandStringsRespectTheLanguageOverride(string language, string expectedActiveEditorLabel)
    {
        var localization = new LocalizationService(language);

        Assert.Equal(expectedActiveEditorLabel, localization.ActiveEditorLabel);
        Assert.False(string.IsNullOrWhiteSpace(localization.SelectCodeOrOpenFileMessage));
        Assert.False(string.IsNullOrWhiteSpace(localization.ImplementWithCodexInstruction));
    }

    [Fact]
    public void ReasoningOptionsKeepXHighMaxAndUltraAsDistinctValues()
    {
        var options = new LocalizationService("en-US")
            .CreateReasoningOptions(new[] { "low", "xhigh", "max", "ultra" });

        Assert.Equal(new[] { "low", "xhigh", "max", "ultra" }, options.Select(option => option.Value));
        Assert.Equal("Extra high", options.Single(option => option.Value == "xhigh").Label);
        Assert.Equal("Maximum", options.Single(option => option.Value == "max").Label);
        Assert.Equal("Ultra", options.Single(option => option.Value == "ultra").Label);
    }
}
