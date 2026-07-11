using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Visual Codex Studio", "Tool window integration for Codex", ExtensionInfo.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(CodexToolWindow))]
[ProvideToolWindow(typeof(CodexSettingsToolWindow))]
[Guid(GuidList.PackageString)]
public sealed class CodexPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        CodexToolWindowManager.Initialize(this);
        await ShowCodexToolWindowCommand.InitializeAsync(this);
        await CodexIdeCommands.InitializeAsync(this);

        if (new ExtensionSettingsStore().Load().OpenOnStartup)
        {
            await CodexToolWindowManager.ShowMainToolWindowAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CodexViewModelHost.Dispose();
        }

        base.Dispose(disposing);
    }
}
