using System;

namespace CodexVsix.UI;

internal enum CodexWebViewHostAttachmentAction
{
    MissingHost,
    FirstAttachment,
    Reuse,
    RecreateWindowedControl
}

internal sealed class CodexWebViewHostAttachmentObservation
{
    public CodexWebViewHostAttachmentObservation(
        CodexWebViewHostAttachmentAction action,
        IntPtr previousRootWindow,
        IntPtr currentRootWindow,
        IntPtr previousNativeHostWindow,
        IntPtr currentNativeHostWindow)
    {
        Action = action;
        PreviousRootWindow = previousRootWindow;
        CurrentRootWindow = currentRootWindow;
        PreviousNativeHostWindow = previousNativeHostWindow;
        CurrentNativeHostWindow = currentNativeHostWindow;
    }

    public CodexWebViewHostAttachmentAction Action { get; }

    public IntPtr PreviousRootWindow { get; }

    public IntPtr CurrentRootWindow { get; }

    public IntPtr PreviousNativeHostWindow { get; }

    public IntPtr CurrentNativeHostWindow { get; }
}

internal sealed class CodexWebViewHostAttachmentTracker
{
    private bool _hasAttachedHost;
    private IntPtr _rootWindow;
    private IntPtr _nativeHostWindow;

    public CodexWebViewHostAttachmentObservation Observe(
        IntPtr rootWindow,
        IntPtr nativeHostWindow,
        CodexOfficialWebViewHostingMode hostingMode)
    {
        var previousRoot = _rootWindow;
        var previousNativeHost = _nativeHostWindow;
        if (rootWindow == IntPtr.Zero)
        {
            return new CodexWebViewHostAttachmentObservation(
                CodexWebViewHostAttachmentAction.MissingHost,
                previousRoot,
                rootWindow,
                previousNativeHost,
                nativeHostWindow);
        }

        if (!_hasAttachedHost)
        {
            _hasAttachedHost = true;
            _rootWindow = rootWindow;
            _nativeHostWindow = nativeHostWindow;
            return new CodexWebViewHostAttachmentObservation(
                CodexWebViewHostAttachmentAction.FirstAttachment,
                previousRoot,
                rootWindow,
                previousNativeHost,
                nativeHostWindow);
        }

        var rootChanged = previousRoot != rootWindow;
        var nativeHostChanged = previousNativeHost != IntPtr.Zero
            && nativeHostWindow != IntPtr.Zero
            && previousNativeHost != nativeHostWindow;
        _rootWindow = rootWindow;
        _nativeHostWindow = nativeHostWindow;
        var action = hostingMode == CodexOfficialWebViewHostingMode.Windowed
            && (rootChanged || nativeHostChanged)
                ? CodexWebViewHostAttachmentAction.RecreateWindowedControl
                : CodexWebViewHostAttachmentAction.Reuse;
        return new CodexWebViewHostAttachmentObservation(
            action,
            previousRoot,
            rootWindow,
            previousNativeHost,
            nativeHostWindow);
    }

    public void ResetAfterRecreation(IntPtr rootWindow, IntPtr nativeHostWindow = default)
    {
        _hasAttachedHost = rootWindow != IntPtr.Zero;
        _rootWindow = rootWindow;
        _nativeHostWindow = nativeHostWindow;
    }

    public void RefreshCurrentHost(IntPtr rootWindow, IntPtr nativeHostWindow)
    {
        if (rootWindow == IntPtr.Zero)
        {
            return;
        }

        _hasAttachedHost = true;
        _rootWindow = rootWindow;
        _nativeHostWindow = nativeHostWindow;
    }
}
