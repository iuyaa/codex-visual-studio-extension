using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexVsix.Services;
using CodexVsix.UI;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public sealed class CodexSettingsToolWindow : ToolWindowPane
{
    private CodexOfficialWebViewHost? _webViewHost;

    public CodexSettingsToolWindow() : base(null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Caption = new LocalizationService().CodexSettingsNav;

        try
        {
            _webViewHost = new CodexOfficialWebViewHost(
                CodexViewModelHost.GetOrCreate(),
                initialRoute: "/settings",
                isSettingsSurface: true,
                registerAsPrimaryHost: false);
            _webViewHost.FallbackRequested += OnWebViewFallbackRequested;
            Content = _webViewHost;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().SettingsToolWindowInitializeLogMessage + Environment.NewLine + ex);
            Content = CreateErrorView(ex);
        }
    }

    internal void ShowSection(string section)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_webViewHost is not null)
        {
            _webViewHost.ShowSettings(section);
            return;
        }

        CodexViewModelHost.GetOrCreate().EnsureExternalSettingsSection(section);
    }

    private void OnWebViewFallbackRequested(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_webViewHost is null)
        {
            return;
        }

        _webViewHost.FallbackRequested -= OnWebViewFallbackRequested;
        _webViewHost.Dispose();
        _webViewHost = null;
        Content = new CodexSettingsToolWindowControl();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _webViewHost is not null)
        {
            _webViewHost.FallbackRequested -= OnWebViewFallbackRequested;
            _webViewHost.Dispose();
            _webViewHost = null;
        }

        base.Dispose(disposing);
    }

    private static FrameworkElement CreateErrorView(Exception ex)
    {
        var localization = new LocalizationService();
        return new Border
        {
            Padding = new Thickness(16),
            Background = Brushes.Transparent,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = localization.SettingsToolWindowErrorMessage
                        + Environment.NewLine
                        + Environment.NewLine
                        + ex.Message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
