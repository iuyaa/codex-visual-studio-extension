using System;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace CodexVsix.UI;

internal enum CodexOfficialWebViewHostingMode
{
    Windowed,
    Composition
}

internal sealed class CodexOfficialWebViewControl : IDisposable
{
    private readonly IDisposable _disposable;

    public CodexOfficialWebViewControl(
        IWebView2 webView,
        FrameworkElement element,
        IDisposable disposable,
        CodexOfficialWebViewHostingMode hostingMode)
    {
        WebView = webView ?? throw new ArgumentNullException(nameof(webView));
        Element = element ?? throw new ArgumentNullException(nameof(element));
        _disposable = disposable ?? throw new ArgumentNullException(nameof(disposable));
        HostingMode = hostingMode;
    }

    public IWebView2 WebView { get; }

    public FrameworkElement Element { get; }

    public CodexOfficialWebViewHostingMode HostingMode { get; }

    public IntPtr NativeHostHandle => WebView is WebView2 windowed ? windowed.Handle : IntPtr.Zero;

    public void Dispose()
    {
        _disposable.Dispose();
    }
}

internal static class CodexOfficialWebViewControlFactory
{
    public static CodexOfficialWebViewControl Create(CodexOfficialWebViewHostingMode hostingMode)
    {
        switch (hostingMode)
        {
            case CodexOfficialWebViewHostingMode.Composition:
                var composition = new WebView2CompositionControl();
                return new CodexOfficialWebViewControl(
                    composition,
                    composition,
                    composition,
                    hostingMode);

            default:
                var windowed = new WebView2();
                return new CodexOfficialWebViewControl(
                    windowed,
                    windowed,
                    windowed,
                    CodexOfficialWebViewHostingMode.Windowed);
        }
    }

    public static CodexOfficialWebViewHostingMode ResolveDefaultHostingMode()
    {
#if DEBUG
        // Developer-only comparison hook. Release builds always use the faster windowed host.
        if (string.Equals(
            Environment.GetEnvironmentVariable("CODEX_VSIX_WEBVIEW_COMPOSITION_BENCHMARK"),
            "1",
            StringComparison.Ordinal))
        {
            return CodexOfficialWebViewHostingMode.Composition;
        }
#endif
        return CodexOfficialWebViewHostingMode.Windowed;
    }

    public static string Format(CodexOfficialWebViewHostingMode hostingMode)
    {
        return hostingMode == CodexOfficialWebViewHostingMode.Composition
            ? "composition"
            : "windowed";
    }
}
