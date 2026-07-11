using CodexVsix.UI;
using Xunit;

namespace CodexVsix.Tests;

public sealed class MarkdownRendererTests
{
    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("http://example.test/")]
    [InlineData("mailto:user@example.test")]
    public void ExternalLinksAllowOnlyExpectedSchemes(string value)
    {
        Assert.True(MarkdownRenderer.TryGetSafeExternalUri(value, out _));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,unsafe")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("C:\\work\\file.cs")]
    public void ExternalLinksRejectExecutableAndLocalSchemes(string value)
    {
        Assert.False(MarkdownRenderer.TryGetSafeExternalUri(value, out _));
    }
}
