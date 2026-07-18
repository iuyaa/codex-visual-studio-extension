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
using System.Windows.Interop;
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

internal sealed class CodexOfficialWebViewReadyEventArgs : EventArgs
{
    public CodexOfficialWebViewReadyEventArgs(TimeSpan readyDuration, string hostingMode)
    {
        ReadyDuration = readyDuration;
        HostingMode = hostingMode ?? string.Empty;
    }

    public TimeSpan ReadyDuration { get; }

    public string HostingMode { get; }
}

internal sealed class CodexOfficialWebViewFallbackEventArgs : EventArgs
{
    public CodexOfficialWebViewFallbackEventArgs(string failureKind, string reason)
    {
        FailureKind = failureKind ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    public string FailureKind { get; }

    public string Reason { get; }
}

internal sealed class CodexOfficialWebViewHost : Grid, IDisposable
{
    private const int MaxQueuedMessages = 100;
    private static readonly object EnvironmentSyncRoot = new();
    private static Task<CoreWebView2Environment>? _sharedEnvironmentTask;
    private static string _sharedEnvironmentDirectory = string.Empty;

    private readonly CodexToolWindowViewModel _viewModel;
    private CodexOfficialWebViewControl _webViewControl;
    private readonly Border _statusLayer;
    private readonly TextBlock _statusText;
    private readonly List<JObject> _queuedMessages = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly string _webviewId = Guid.NewGuid().ToString("N");
    private readonly CodexOfficialWebViewBridge _bridge;
    private readonly CodexOfficialWebViewHostingMode _hostingMode;
    private readonly CodexWebViewHostAttachmentTracker _hostAttachmentTracker = new();
    private readonly Stopwatch _initializationStopwatch = new();
    private readonly bool _isSettingsSurface;
    private readonly bool _refreshToolWindowStartupState;
    private string _currentRoute;
    private bool _initialized;
    private bool _ready;
    private bool _readyReported;
    private bool _disposed;
    private bool _themeSubscribed;
    private bool _fallbackRequested;
    private int _initializationGeneration;
    private int _hostLoadGeneration;

    public CodexOfficialWebViewHost(
        CodexToolWindowViewModel viewModel,
        string initialRoute = "/",
        bool isSettingsSurface = false,
        bool registerAsPrimaryHost = true,
        CodexOfficialWebViewHostingMode? hostingMode = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _viewModel = viewModel;
        _currentRoute = NormalizeRoute(initialRoute);
        _isSettingsSurface = isSettingsSurface;
        _refreshToolWindowStartupState = registerAsPrimaryHost;
        _hostingMode = hostingMode ?? CodexOfficialWebViewControlFactory.ResolveDefaultHostingMode();
        _webViewControl = CodexOfficialWebViewControlFactory.Create(_hostingMode);
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

        Children.Add(_webViewControl.Element);
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
            isSettingsSurface,
            UpdateCurrentRoute);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        PresentationSource.AddSourceChangedHandler(this, OnPresentationSourceChanged);
        CodexDiagnosticLogger.Shared.EnabledChanged += OnDiagnosticLoggingChanged;
        CodexOfficialWebViewHostRegistry.Register(this, registerAsPrimaryHost);
        CodexDiagnosticLogger.Shared.Write(
            "webview.host.created",
            new JObject
            {
                ["hostingMode"] = HostingModeName,
                ["settingsSurface"] = _isSettingsSurface,
                ["initialRoute"] = _currentRoute
            });
    }

    public bool IsReady => _ready;

    private IWebView2 WebView => _webViewControl.WebView;

    private string HostingModeName => CodexOfficialWebViewControlFactory.Format(_hostingMode);

    public event EventHandler<CodexOfficialWebViewReadyEventArgs>? Ready;

    public event EventHandler<CodexOfficialWebViewFallbackEventArgs>? FallbackRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_refreshToolWindowStartupState)
        {
            _viewModel.EnsureToolWindowStartupState();
        }

        ScheduleHostObservation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hostLoadGeneration++;
        CodexDiagnosticLogger.Shared.Write(
            "webview.host.unloaded",
            CreateHostDiagnosticDetails());
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!CodexDiagnosticLogger.Shared.IsEnabled)
        {
            return;
        }

        CodexDiagnosticLogger.Shared.Write(
            "webview.host.size-changed",
            new JObject
            {
                ["width"] = Math.Round(e.NewSize.Width, 1),
                ["height"] = Math.Round(e.NewSize.Height, 1),
                ["loaded"] = IsLoaded,
                ["visible"] = IsVisible
            });
    }

    private void OnPresentationSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        CodexDiagnosticLogger.Shared.Write(
            "webview.host.presentation-source-changed",
            new JObject
            {
                ["oldRootHwnd"] = FormatHandle((e.OldSource as HwndSource)?.Handle ?? IntPtr.Zero),
                ["newRootHwnd"] = FormatHandle((e.NewSource as HwndSource)?.Handle ?? IntPtr.Zero),
                ["loaded"] = IsLoaded,
                ["visible"] = IsVisible
            });
        ScheduleHostObservation();
    }

    private void ScheduleHostObservation()
    {
        var loadGeneration = ++_hostLoadGeneration;
        ObserveHostAndInitializeAsync(loadGeneration)
            .FileAndForget("CodexVsix/OfficialWebViewHostAttach");
    }

    private async Task ObserveHostAndInitializeAsync(int loadGeneration)
    {
        await Task.Yield();
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
        if (_disposed || !IsLoaded || loadGeneration != _hostLoadGeneration)
        {
            return;
        }

        var rootWindow = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
        var observation = _hostAttachmentTracker.Observe(
            rootWindow,
            _webViewControl.NativeHostHandle,
            _hostingMode);
        CodexDiagnosticLogger.Shared.Write(
            "webview.host.attached",
            new JObject
            {
                ["action"] = observation.Action.ToString(),
                ["hostingMode"] = HostingModeName,
                ["previousRootHwnd"] = FormatHandle(observation.PreviousRootWindow),
                ["rootHwnd"] = FormatHandle(observation.CurrentRootWindow),
                ["previousNativeHwnd"] = FormatHandle(observation.PreviousNativeHostWindow),
                ["nativeHwnd"] = FormatHandle(observation.CurrentNativeHostWindow)
            });

        if (observation.Action == CodexWebViewHostAttachmentAction.MissingHost)
        {
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
            if (_disposed || !IsLoaded || loadGeneration != _hostLoadGeneration)
            {
                return;
            }

            rootWindow = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            observation = _hostAttachmentTracker.Observe(
                rootWindow,
                _webViewControl.NativeHostHandle,
                _hostingMode);
        }

        if (observation.Action == CodexWebViewHostAttachmentAction.RecreateWindowedControl
            && _initialized)
        {
            RecreateWebViewForHostChange(rootWindow);
        }

        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _initialized = true;
        var generation = ++_initializationGeneration;
        var webViewControl = _webViewControl;
        var webView = webViewControl.WebView;
        _initializationStopwatch.Restart();
        CodexDiagnosticLogger.Shared.Write(
            "webview.initialize.started",
            CreateHostDiagnosticDetails());
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
            var userDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexVsix",
                "WebView2");
            Directory.CreateDirectory(userDataDirectory);
            var environment = await GetSharedEnvironmentAsync(userDataDirectory);
            await webView.EnsureCoreWebView2Async(environment);
            if (_disposed
                || generation != _initializationGeneration
                || !ReferenceEquals(webViewControl, _webViewControl))
            {
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
                _currentRoute,
                _viewModel.Settings.EnableDiagnosticLogging);
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
            var currentRootWindow = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            _hostAttachmentTracker.RefreshCurrentHost(
                currentRootWindow,
                _webViewControl.NativeHostHandle);
            CodexDiagnosticLogger.Shared.Write(
                "webview.initialize.navigation-started",
                new JObject
                {
                    ["hostingMode"] = HostingModeName,
                    ["durationMilliseconds"] = _initializationStopwatch.ElapsedMilliseconds,
                    ["runtimeVersion"] = environment.BrowserVersionString
                });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed
                && generation == _initializationGeneration
                && ReferenceEquals(webViewControl, _webViewControl))
            {
                ShowFailure("initialization", ex);
            }
        }
    }

    private void ConfigureCoreWebView(IWebView2 webView)
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

    private static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync(
        string userDataDirectory)
    {
        Task<CoreWebView2Environment> environmentTask;
        lock (EnvironmentSyncRoot)
        {
            if (_sharedEnvironmentTask is null
                || !string.Equals(
                    _sharedEnvironmentDirectory,
                    userDataDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                _sharedEnvironmentDirectory = userDataDirectory;
                _sharedEnvironmentTask = CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDirectory);
            }

            environmentTask = _sharedEnvironmentTask;
        }

        try
        {
            return await environmentTask;
        }
        catch
        {
            lock (EnvironmentSyncRoot)
            {
                if (ReferenceEquals(_sharedEnvironmentTask, environmentTask))
                {
                    _sharedEnvironmentTask = null;
                    _sharedEnvironmentDirectory = string.Empty;
                }
            }

            throw;
        }
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
                ReportReadyOnce();
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
            ShowFailure("navigation", new InvalidOperationException(
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
        ShowFailure("process", new InvalidOperationException(
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

        if (WebView.CoreWebView2 is null || !_ready)
        {
            if (_queuedMessages.Count >= MaxQueuedMessages)
            {
                _queuedMessages.RemoveAt(0);
            }

            _queuedMessages.Add((JObject)message.DeepClone());
            return;
        }

        WebView.CoreWebView2.PostWebMessageAsJson(message.ToString(Formatting.None));
    }

    private async Task PostMessageOnUiThreadAsync(JObject message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
        PostMessage(message);
    }

    private void FlushQueuedMessages()
    {
        if (!_ready || WebView.CoreWebView2 is null)
        {
            return;
        }

        foreach (var message in _queuedMessages)
        {
            WebView.CoreWebView2.PostWebMessageAsJson(message.ToString(Formatting.None));
        }

        _queuedMessages.Clear();
    }

    private void UnsubscribeAndDispose(CodexOfficialWebViewControl webViewControl)
    {
        var webView = webViewControl.WebView;
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

        webViewControl.Dispose();
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
        _currentRoute = NormalizeRoute(path);
        var state = new JObject();
        if (focusComposer)
        {
            state["focusComposerNonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        PostMessage(new JObject
        {
            ["type"] = "navigate-to-route",
            ["path"] = _currentRoute,
            ["state"] = state
        });
    }

    private void ShowFailure(string? failureKind, Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            ShowFailureOnUiThreadAsync(failureKind, exception)
                .FileAndForget("CodexVsix/OfficialWebViewFailure");
            return;
        }

        ActivityLog.TryLogError("CodexVsix", "[official webview] " + exception);
        _bridge.DisableInteractiveServerRequests();
        _statusText.Text = "The official Codex interface could not be loaded.\n\n"
            + exception.Message
            + "\n\nThe classic Visual Studio interface remains available.";
        _statusLayer.Visibility = Visibility.Visible;
        CodexDiagnosticLogger.Shared.Write(
            "webview.failure",
            new JObject
            {
                ["failureKind"] = failureKind ?? string.Empty,
                ["error"] = exception.Message,
                ["hostingMode"] = HostingModeName
            });
        RequestFallback(failureKind ?? "official-failure", exception.Message);
    }

    private void RequestFallback(string failureKind, string reason)
    {
        var handler = FallbackRequested;
        if (_fallbackRequested || handler is null)
        {
            return;
        }

        _fallbackRequested = true;
        handler(this, new CodexOfficialWebViewFallbackEventArgs(failureKind, reason));
    }

    private async Task ShowFailureOnUiThreadAsync(string? failureKind, Exception exception)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_lifetimeCts.Token);
        ShowFailure(failureKind, exception);
    }

    private void RecreateWebViewForHostChange(IntPtr currentRootWindow)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var previousControl = _webViewControl;
        _initializationGeneration++;
        _initialized = false;
        _ready = false;
        _readyReported = false;
        _bridge.DisableInteractiveServerRequests();
        _statusText.Text = "Loading Codex…";
        _statusLayer.Visibility = Visibility.Visible;

        Children.Remove(previousControl.Element);
        UnsubscribeAndDispose(previousControl);
        _webViewControl = CodexOfficialWebViewControlFactory.Create(_hostingMode);
        Children.Insert(0, _webViewControl.Element);
        _hostAttachmentTracker.ResetAfterRecreation(
            currentRootWindow,
            _webViewControl.NativeHostHandle);
        CodexDiagnosticLogger.Shared.Write(
            "webview.host.recreated",
            new JObject
            {
                ["reason"] = "host-window-changed",
                ["hostingMode"] = HostingModeName,
                ["rootHwnd"] = FormatHandle(currentRootWindow),
                ["route"] = _currentRoute
            });
    }

    private void ReportReadyOnce()
    {
        if (_readyReported)
        {
            return;
        }

        _readyReported = true;
        _initializationStopwatch.Stop();
        CodexDiagnosticLogger.Shared.Write(
            "webview.ready",
            new JObject
            {
                ["hostingMode"] = HostingModeName,
                ["durationMilliseconds"] = _initializationStopwatch.ElapsedMilliseconds,
                ["route"] = _currentRoute,
                ["width"] = Math.Round(ActualWidth, 1),
                ["height"] = Math.Round(ActualHeight, 1)
            });
        Ready?.Invoke(
            this,
            new CodexOfficialWebViewReadyEventArgs(
                _initializationStopwatch.Elapsed,
                HostingModeName));
    }

    private void UpdateCurrentRoute(string route)
    {
        _currentRoute = NormalizeRoute(route);
    }

    private void OnDiagnosticLoggingChanged(
        object? sender,
        CodexDiagnosticLoggingChangedEventArgs e)
    {
        PostMessage(new JObject
        {
            ["type"] = "diagnostic-logging-changed",
            ["enabled"] = e.Enabled
        });
    }

    private JObject CreateHostDiagnosticDetails()
    {
        var rootWindow = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
        return new JObject
        {
            ["hostingMode"] = HostingModeName,
            ["rootHwnd"] = FormatHandle(rootWindow),
            ["nativeHwnd"] = FormatHandle(_webViewControl.NativeHostHandle),
            ["loaded"] = IsLoaded,
            ["visible"] = IsVisible,
            ["width"] = Math.Round(ActualWidth, 1),
            ["height"] = Math.Round(ActualHeight, 1)
        };
    }

    private static string FormatHandle(IntPtr handle)
    {
        return "0x" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
    }

    private static string NormalizeRoute(string? route)
    {
        var value = string.IsNullOrWhiteSpace(route) ? "/" : route!.Trim();
        return value[0] == '/' ? value : "/" + value;
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
        Unloaded -= OnUnloaded;
        SizeChanged -= OnSizeChanged;
        PresentationSource.RemoveSourceChangedHandler(this, OnPresentationSourceChanged);
        CodexDiagnosticLogger.Shared.EnabledChanged -= OnDiagnosticLoggingChanged;
        _hostLoadGeneration++;
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

        UnsubscribeAndDispose(_webViewControl);
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
