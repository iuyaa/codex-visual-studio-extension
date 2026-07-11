using System;
using System.Threading.Tasks;
using CodexVsix.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodexVsix;

internal static class CodexToolWindowManager
{
    private static AsyncPackage? _package;
    private static ToolWindowPane? _settingsWindow;

    internal static VSFRAMEMODE SettingsFrameMode => VSFRAMEMODE.VSFM_MdiChild;

    public static void Initialize(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _package = package;
    }

    public static void ShowSettingsToolWindow(string section)
    {
        ShowSettingsToolWindowAsync(section).FileAndForget("CodexVsix/ShowSettingsToolWindow");
    }

    public static async Task<ToolWindowPane?> ShowMainToolWindowAsync()
    {
        var package = _package;
        if (package is null)
        {
            return null;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var window = await package.FindToolWindowAsync(typeof(CodexToolWindow), 0, true, package.DisposalToken);
        if (window?.Frame is Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame frame)
        {
            frame.Show();
        }

        return window;
    }

    private static async Task ShowSettingsToolWindowAsync(string section)
    {
        var package = _package;
        if (package is null)
        {
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        try
        {
            var window = await package.FindToolWindowAsync(
                typeof(CodexSettingsToolWindow),
                0,
                true,
                package.DisposalToken);
            if (window?.Frame is not IVsWindowFrame frame)
            {
                throw new NotSupportedException(new LocalizationService().SettingsToolWindowErrorMessage);
            }

            if (window is CodexSettingsToolWindow settingsWindow)
            {
                settingsWindow.ShowSection(section);
            }

            _settingsWindow = window;
            UpdateWindowCaption(window, CodexViewModelHost.GetOrCreate().Localization);
            ErrorHandler.ThrowOnFailure(frame.SetProperty(
                (int)__VSFPROPID.VSFPROPID_FrameMode,
                SettingsFrameMode));
            ErrorHandler.ThrowOnFailure(frame.Show());
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().SettingsToolWindowOpenLogMessage + Environment.NewLine + ex);
        }
    }

    public static void RefreshSettingsToolWindowCaption(Services.LocalizationService localization)
    {
        RefreshSettingsToolWindowCaptionAsync(localization).FileAndForget("CodexVsix/RefreshSettingsCaption");
    }

    private static async Task RefreshSettingsToolWindowCaptionAsync(Services.LocalizationService localization)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (_settingsWindow is not null)
        {
            UpdateWindowCaption(_settingsWindow, localization);
        }
    }

    private static void UpdateWindowCaption(ToolWindowPane window, Services.LocalizationService localization)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        window.Caption = localization.CodexSettingsNav;
    }
}
