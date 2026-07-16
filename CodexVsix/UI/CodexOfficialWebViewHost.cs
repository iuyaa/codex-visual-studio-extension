using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexVsix.Services;
using CodexVsix.ViewModels;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.UI;

internal sealed class CodexOfficialWebViewHost : Grid, IDisposable
{
    private const int MaxQueuedMessages = 100;

    private readonly CodexToolWindowViewModel _viewModel;
    private WebView2 _webView = new();
    private readonly Border _statusLayer;
    private readonly TextBlock _statusText;
    private readonly List<JObject> _queuedMessages = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly string _webviewId = Guid.NewGuid().ToString("N");
    private readonly CodexOfficialWebViewBridge _bridge;
    private readonly string _initialRoute;
    private readonly bool _isSettingsSurface;
    private readonly bool _refreshToolWindowStartupState;
    private bool _initialized;
    private bool _ready;
    private bool _disposed;
    private bool _themeSubscribed;
    private bool _fallbackRequested;
    private int _initializationGeneration;

    public CodexOfficialWebViewHost(
        CodexToolWindowViewModel viewModel,
        string initialRoute = "/",
        bool isSettingsSurface = false,
        bool registerAsPrimaryHost = true)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _viewModel = viewModel;
        _initialRoute = string.IsNullOrWhiteSpace(initialRoute) ? "/" : initialRoute;
        _isSettingsSurface = isSettingsSurface;
        _refreshToolWindowStartupState = registerAsPrimaryHost;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

        Children.Add(_webView);
        _statusText = new TextBlock
        {
            Text = "Loading Codex…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24)
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _statusLayer = new Border
        {
            Child = _statusText,
            Background = Brushes.Transparent
        };
        SetZIndex(_statusLayer, 10);
        Children.Add(_statusLayer);

        _bridge = new CodexOfficialWebViewBridge(
            viewModel,
            PostMessage,
            section => global::CodexVsix.CodexToolWindowManager.ShowSettingsToolWindow(section),
            queryKey => CodexOfficialWebViewHostRegistry.BroadcastQueryInvalidation(this, queryKey),
            isSettingsSurface);
        Loaded += OnLoaded;
        CodexOfficialWebViewHostRegistry.Register(this, registerAsPrimaryHost);
    }

    public bool IsReady => _ready;

    public event EventHandler? FallbackRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_refreshToolWindowStartupState)
        {
            _viewModel.EnsureToolWindowStartupState();
        }

        InitializeAsync().FileAndForget("CodexVsix/OfficialWebViewInitialize");
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _initialized = true;
        var generation = ++_initializationGeneration;
        var webView = _webView;
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
            var userDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexVsix",
                "WebView2");
            Directory.CreateDirectory(userDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDirectory);
            await webView.EnsureCoreWebView2Async(environment);
            if (_disposed
                || generation != _initializationGeneration
                || !ReferenceEquals(webView, _webView))
            {
                webView.Dispose();
                return;
            }

            ConfigureCoreWebView(webView);

            var resourceRoot = ResolveResourceRoot();
            var shellDirectory = Path.Combine(userDataDirectory, "shell");
            Directory.CreateDirectory(shellDirectory);
            var shellFileName = "shell-" + _webviewId + ".html";
            var shellPath = Path.Combine(shellDirectory, shellFileName);
            var theme = CodexVisualStudioTheme.Capture();
            var html = CodexOfficialWebViewShell.Build(
                resourceRoot,
                _webviewId,
                ResolveLocale(),
                theme,
                _initialRoute);
            File.WriteAllText(shellPath, html);

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                CodexOfficialWebViewShell.AssetHostName,
                resourceRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                CodexOfficialWebViewShell.ShellHostName,
                shellDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            SubscribeToThemeChanges();
            webView.Source = new Uri(
                "https://" + CodexOfficialWebViewShell.ShellHostName + "/" + shellFileName + "?webviewId=" + Uri.EscapeDataString(_webviewId));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed
                && generation == _initializationGeneration
                && ReferenceEquals(webView, _webView))
            {
                ShowFailure(ex);
            }
        }
    }

    private void ConfigureCoreWebView(WebView2 webView)
    {
        var core = webView.CoreWebView2;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        HandleWebMessageReceivedAsync(e).FileAndForget("CodexVsix/OfficialWebViewMessage");
    }

    private async Task HandleWebMessageReceivedAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var envelope = JObject.Parse(e.WebMessageAsJson);
            var type = envelope["message"]?["type"]?.Value<string>();
            if (string.Equals(type, "webview-ready", StringComparison.Ordinal)
                || string.Equals(type, "ready", StringComparison.Ordinal))
            {
                _ready = true;
                _statusLayer.Visibility = Visibility.Collapsed;
                FlushQueuedMessages();
                _bridge.EnableInteractiveServerRequests();
            }

            await _bridge.HandleEnvelopeAsync(envelope, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", "[official webview message] " + ex);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowFailure(new InvalidOperationException(
                "The Codex webview navigation failed: " + e.WebErrorStatus));
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            || string.Equals(uri.Host, CodexOfficialWebViewShell.AssetHostName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, CodexOfficialWebViewShell.ShellHostName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        OpenExternalUri(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri(e.Uri);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ShowFailure(new InvalidOperationException(
            "The Codex webview process failed: " + e.ProcessFailedKind));
    }

    private void SubscribeToThemeChanges()
    {
        if (_themeSubscribed)
        {
            return;
        }

        VSColorTheme.ThemeChanged += OnVisualStudioThemeChanged;
        _themeSubscribed = true;
    }

    private void OnVisualStudioThemeChanged(ThemeChangedEventArgs e)
    {
        _bridge.PostTheme();
    }

    private void PostMessage(JObject message)
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            PostMessageOnUiThreadAsync((JObject)message.DeepClone())
                .FileAndForget("CodexVsix/OfficialWebViewPostMessage");
            return;
        }

        if (_webView.CoreWebView2 is null || !_ready)
        {
            if (_queuedMessages.Count >= MaxQueuedMessages)
            {
                _queuedMessages.RemoveAt(0);
            }

            _queuedMessages.Add((JObject)message.DeepClone());
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsJson(message.ToString(Formatting.None));
    }

    private async Task PostMessageOnUiThreadAsync(JObject message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
        PostMessage(message);
    }

    private void FlushQueuedMessages()
    {
        if (!_ready || _webView.CoreWebView2 is null)
        {
            return;
        }

        foreach (var message in _queuedMessages)
        {
            _webView.CoreWebView2.PostWebMessageAsJson(message.ToString(Formatting.None));
        }

        _queuedMessages.Clear();
    }

    private void UnsubscribeAndDispose(WebView2 webView)
    {
        try
        {
            var core = webView.CoreWebView2;
            if (core is not null)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NavigationStarting -= OnNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.ProcessFailed -= OnProcessFailed;
            }
        }
        catch (InvalidOperationException)
        {
        }

        webView.Dispose();
    }

    public void PrefillComposer(string text)
    {
        _bridge.HideHistoryWindow();
        _bridge.PostSharedObject(
            "composer_prefill",
            new JObject
            {
                ["text"] = text ?? string.Empty,
                ["cwd"] = ResolveWorkingDirectory()
            });
        NavigateTo("/", focusComposer: true);
    }

    public void StartNewConversation()
    {
        _bridge.HideHistoryWindow();
        NavigateTo("/", focusComposer: true);
    }

    public void ShowSettings(string section = "")
    {
        var normalized = (section ?? string.Empty).Trim().Trim('/');
        if (!_isSettingsSurface)
        {
            global::CodexVsix.CodexToolWindowManager.ShowSettingsToolWindow(normalized);
            return;
        }

        NavigateTo(string.IsNullOrWhiteSpace(normalized) ? "/settings" : "/settings/" + normalized, focusComposer: false);
    }

    internal void PostQueryCacheInvalidation(JToken queryKey)
    {
        PostMessage(CodexOfficialWebViewBridge.CreateQueryInvalidationNotification(queryKey));
    }

    public void FocusComposer()
    {
        NavigateTo("/", focusComposer: true);
    }

    private void NavigateTo(string path, bool focusComposer)
    {
        var state = new JObject();
        if (focusComposer)
        {
            state["focusComposerNonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        PostMessage(new JObject
        {
            ["type"] = "navigate-to-route",
            ["path"] = path,
            ["state"] = state
        });
    }

    private void ShowFailure(Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            ShowFailureOnUiThreadAsync(exception)
                .FileAndForget("CodexVsix/OfficialWebViewFailure");
            return;
        }

        ActivityLog.TryLogError("CodexVsix", "[official webview] " + exception);
        _bridge.DisableInteractiveServerRequests();
        _statusText.Text = "The official Codex interface could not be loaded.\n\n"
            + exception.Message
            + "\n\nThe classic Visual Studio interface remains available.";
        _statusLayer.Visibility = Visibility.Visible;
        RequestFallback();
    }

    private void RequestFallback()
    {
        var handler = FallbackRequested;
        if (_fallbackRequested || handler is null)
        {
            return;
        }

        _fallbackRequested = true;
        handler(this, EventArgs.Empty);
    }

    private async Task ShowFailureOnUiThreadAsync(Exception exception)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
        ShowFailure(exception);
    }

    private string ResolveWorkingDirectory()
    {
        var path = _viewModel.Settings.WorkingDirectory;
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string ResolveLocale()
    {
        return string.IsNullOrWhiteSpace(_viewModel.Settings.LanguageOverride)
            ? CultureInfo.CurrentUICulture.Name
            : _viewModel.Settings.LanguageOverride;
    }

    private static string ResolveResourceRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppDomain.CurrentDomain.BaseDirectory;
        var root = Path.Combine(assemblyDirectory, "UI", "CodexWebview");
        if (!File.Exists(Path.Combine(root, "webview", "index.html")))
        {
            throw new DirectoryNotFoundException(
                "The official Codex webview assets were not found at " + root);
        }

        return root;
    }

    private static void OpenExternalUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", "[official webview external navigation] " + ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        _initializationGeneration++;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _bridge.Dispose();
        CodexOfficialWebViewHostRegistry.Unregister(this);
        if (_themeSubscribed)
        {
            VSColorTheme.ThemeChanged -= OnVisualStudioThemeChanged;
            _themeSubscribed = false;
        }

        UnsubscribeAndDispose(_webView);
    }
}

internal static class CodexOfficialWebViewHostRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly List<WeakReference<CodexOfficialWebViewHost>> Hosts = new();
    private static WeakReference<CodexOfficialWebViewHost>? _primary;

    public static void Register(CodexOfficialWebViewHost host, bool isPrimary)
    {
        lock (SyncRoot)
        {
            RemoveDeadHosts();
            if (!Hosts.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, host)))
            {
                Hosts.Add(new WeakReference<CodexOfficialWebViewHost>(host));
            }

            if (isPrimary)
            {
                _primary = new WeakReference<CodexOfficialWebViewHost>(host);
            }
        }
    }

    public static void Unregister(CodexOfficialWebViewHost host)
    {
        lock (SyncRoot)
        {
            Hosts.RemoveAll(reference => !reference.TryGetTarget(out var existing) || ReferenceEquals(existing, host));
            if (_primary is not null
                && (!_primary.TryGetTarget(out var primary) || ReferenceEquals(primary, host)))
            {
                _primary = null;
            }
        }
    }

    public static void BroadcastQueryInvalidation(CodexOfficialWebViewHost source, JToken queryKey)
    {
        List<CodexOfficialWebViewHost> targets;
        lock (SyncRoot)
        {
            RemoveDeadHosts();
            targets = Hosts
                .Select(reference => reference.TryGetTarget(out var host) ? host : null)
                .Where(host => host is not null && !ReferenceEquals(host, source))
                .Cast<CodexOfficialWebViewHost>()
                .ToList();

            if (_primary is not null
                && _primary.TryGetTarget(out var primary)
                && !ReferenceEquals(primary, source)
                && !targets.Contains(primary))
            {
                targets.Add(primary);
            }
        }

        foreach (var target in targets)
        {
            target.PostQueryCacheInvalidation(queryKey);
        }
    }

    public static bool TryPrefillComposer(string text)
    {
        if (!TryGet(out var host))
        {
            return false;
        }

        RunOnUiThreadAsync(() => host.PrefillComposer(text), "PrefillComposer")
            .FileAndForget("CodexVsix/OfficialWebViewPrefillComposer");
        return true;
    }

    public static bool TryStartNewConversation()
    {
        if (!TryGet(out var host))
        {
            return false;
        }

        RunOnUiThreadAsync(host.StartNewConversation, "StartNewConversation")
            .FileAndForget("CodexVsix/OfficialWebViewStartNewConversation");
        return true;
    }

    public static bool TryFocusComposer()
    {
        if (!TryGet(out var host))
        {
            return false;
        }

        RunOnUiThreadAsync(host.FocusComposer, "FocusComposer")
            .FileAndForget("CodexVsix/OfficialWebViewFocusComposer");
        return true;
    }

    private static async Task RunOnUiThreadAsync(Action action, string operationName)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", operationName + Environment.NewLine + ex);
        }
    }

    private static bool TryGet(out CodexOfficialWebViewHost host)
    {
        lock (SyncRoot)
        {
            host = null!;
            return _primary is not null
                && _primary.TryGetTarget(out host)
                && host.Visibility == Visibility.Visible;
        }
    }

    private static void RemoveDeadHosts()
    {
        Hosts.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
}
