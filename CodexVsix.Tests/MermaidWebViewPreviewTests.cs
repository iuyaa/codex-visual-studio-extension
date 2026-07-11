using CodexVsix.Services;
using CodexVsix.UI;
using Xunit;

namespace CodexVsix.Tests;

public sealed class MermaidWebViewPreviewTests
{
    [Fact]
    public void HostHtmlJsonEscapesLocalizedJavascriptFallbacks()
    {
        var html = MermaidWebViewPreview.BuildHostHtml(new LocalizationService("fr-FR"));

        Assert.Contains("|| \"Impossible de générer l'aperçu Mermaid.\"", html);
        Assert.Contains("|| \"Erreur lors du chargement de l'aperçu Mermaid.\"", html);
        Assert.DoesNotContain("|| 'Impossible de générer l'aperçu Mermaid.'", html);
    }
}
