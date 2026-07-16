using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexVsix.Services;
using CodexVsix.UI;
using CodexVsix.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public sealed class CodexToolWindow : ToolWindowPane
{
    private CodexOfficialWebViewHost? _webViewHost;
    private CodexToolWindowControl? _classicControl;

    public CodexToolWindow() : base(null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Caption = "Codex";

        CodexToolWindowViewModel viewModel;
        try
        {
            viewModel = CodexViewModelHost.GetOrCreate();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().ToolWindowViewModelCreateLogMessage + Environment.NewLine + ex);
            Content = CreateErrorView(ex);
            return;
        }

        try
        {
            _webViewHost = new CodexOfficialWebViewHost(viewModel);
            _webViewHost.FallbackRequested += OnWebViewFallbackRequested;
            Content = _webViewHost;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().ToolWindowInitializeLogMessage + Environment.NewLine + ex);
            ShowClassicFallback();
        }
    }

    private void OnWebViewFallbackRequested(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ShowClassicFallback();
    }

    private void ShowClassicFallback()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        DisposeWebViewHost();

        try
        {
            _classicControl = new CodexToolWindowControl();
            Content = _classicControl;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().ToolWindowInitializeLogMessage + Environment.NewLine + ex);
            Content = CreateErrorView(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeWebViewHost();
            _classicControl?.Dispose();
            _classicControl = null;
        }

        base.Dispose(disposing);
    }

    private void DisposeWebViewHost()
    {
        if (_webViewHost is null)
        {
            return;
        }

        _webViewHost.FallbackRequested -= OnWebViewFallbackRequested;
        _webViewHost.Dispose();
        _webViewHost = null;
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
                    Text = localization.ToolWindowErrorMessage
                        + Environment.NewLine
                        + Environment.NewLine
                        + ex.Message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
