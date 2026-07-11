using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodexVsix.Services;
using CodexVsix.UI;
using CodexVsix.ViewModels;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodexVsix;

internal sealed class CodexIdeCommands
{
    private readonly AsyncPackage _package;
    private readonly SolutionContextService _solutionContextService = new();

    private CodexIdeCommands(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        AddCommand(commandService, PackageIds.OpenSidebarCommand, ExecuteOpenSidebarAsync);
        AddCommand(commandService, PackageIds.NewCodexAgentCommand, ExecuteNewAgentAsync);
        AddCommand(commandService, PackageIds.AddSelectionToThreadCommand, ExecuteAddSelectionAsync);
        AddCommand(commandService, PackageIds.AddFileToThreadCommand, ExecuteAddFileAsync);
        AddCommand(commandService, PackageIds.ReviewSelectionCommand, ExecuteReviewSelectionAsync);
        AddCommand(commandService, PackageIds.ImplementTodoCommand, ExecuteImplementTodoAsync);
        AddCommand(commandService, PackageIds.OpenCodexSettingsCommand, ExecuteOpenSettingsAsync);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService is not null)
        {
            _ = new CodexIdeCommands(package, commandService);
        }
    }

    private static void AddCommand(OleMenuCommandService commandService, int commandId, Func<Task> executeAsync)
    {
        var menuCommand = new MenuCommand(
            (_, _) => executeAsync().FileAndForget("CodexVsix/IdeCommand"),
            new CommandID(new Guid(GuidList.CommandSetString), commandId));
        commandService.AddCommand(menuCommand);
    }

    private async Task ExecuteOpenSidebarAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        if (!CodexOfficialWebViewHostRegistry.TryFocusComposer())
        {
            viewModel.RequestComposerFocus();
        }
    }

    private async Task ExecuteNewAgentAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        if (!CodexOfficialWebViewHostRegistry.TryStartNewConversation())
        {
            viewModel.StartNewAgentFromIdeCommand();
        }
    }

    private async Task ExecuteAddSelectionAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        var context = BuildSelectionContext(viewModel, includeInstruction: false);
        if (string.IsNullOrWhiteSpace(context))
        {
            ShowMessage(viewModel.Localization.SelectCodeOrOpenFileMessage);
            viewModel.RequestComposerFocus();
            return;
        }

        if (!CodexOfficialWebViewHostRegistry.TryPrefillComposer(context))
        {
            viewModel.AppendComposerContextFromIdeCommand(context);
        }
    }

    private async Task ExecuteAddFileAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        var context = BuildFileContext(viewModel);
        if (string.IsNullOrWhiteSpace(context))
        {
            ShowMessage(viewModel.Localization.SelectFileOrOpenFileMessage);
            viewModel.RequestComposerFocus();
            return;
        }

        if (!CodexOfficialWebViewHostRegistry.TryPrefillComposer(context))
        {
            viewModel.AppendComposerContextFromIdeCommand(context);
        }
    }

    private async Task ExecuteReviewSelectionAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        var context = BuildSelectionContext(viewModel, includeInstruction: true);
        if (string.IsNullOrWhiteSpace(context))
        {
            ShowMessage(viewModel.Localization.SelectCodeForReviewMessage);
            viewModel.RequestComposerFocus();
            return;
        }

        var prompt = "/review" + Environment.NewLine + Environment.NewLine + context;
        if (CodexOfficialWebViewHostRegistry.TryPrefillComposer(prompt))
        {
            return;
        }

        if (string.Equals(viewModel.Settings.ReviewDelivery, "detached", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.StartNewAgentFromIdeCommand();
        }

        viewModel.ReplaceComposerPromptFromIdeCommand(prompt);
    }

    private async Task ExecuteImplementTodoAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var viewModel = await ShowCodexAsync();
        var context = BuildSelectionContext(viewModel, includeInstruction: true);
        if (string.IsNullOrWhiteSpace(context))
        {
            context = BuildFileContext(viewModel);
        }

        if (string.IsNullOrWhiteSpace(context))
        {
            ShowMessage(viewModel.Localization.OpenFileOrSelectTodoMessage);
            viewModel.RequestComposerFocus();
            return;
        }

        var prompt = viewModel.Localization.ImplementWithCodexInstruction + Environment.NewLine + Environment.NewLine + context;
        if (!CodexOfficialWebViewHostRegistry.TryPrefillComposer(prompt))
        {
            viewModel.ReplaceComposerPromptFromIdeCommand(prompt);
        }
    }

    private async Task ExecuteOpenSettingsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        CodexToolWindowManager.ShowSettingsToolWindow("agent");
    }

    private async Task<CodexToolWindowViewModel> ShowCodexAsync()
    {
        await CodexToolWindowManager.ShowMainToolWindowAsync();
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        return CodexViewModelHost.GetOrCreate();
    }

    private string BuildSelectionContext(CodexToolWindowViewModel viewModel, bool includeInstruction)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var selection = _solutionContextService.GetActiveSelectionSnippetForPrompt();
        if (string.IsNullOrWhiteSpace(selection))
        {
            return string.Empty;
        }

        var workingDirectory = viewModel.Settings.WorkingDirectory;
        var activeDocument = _solutionContextService.GetActiveDocumentPath();
        var relativePath = string.IsNullOrWhiteSpace(activeDocument)
            ? viewModel.Localization.ActiveEditorLabel
            : _solutionContextService.FormatPathForPrompt(workingDirectory, activeDocument!);

        var builder = new System.Text.StringBuilder();
        if (includeInstruction)
        {
            builder.AppendLine(viewModel.Localization.IdeSelectionInstruction);
            builder.AppendLine();
        }

        builder.AppendLine("File: " + FormatFileMention(relativePath));
        builder.AppendLine();
        builder.AppendLine(BuildFencedCode(relativePath, selection));
        return builder.ToString().Trim();
    }

    private string BuildFileContext(CodexToolWindowViewModel viewModel)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var workingDirectory = viewModel.Settings.WorkingDirectory;
        var selectedItem = _solutionContextService.GetSelectedItemPathsForPrompt(workingDirectory).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(selectedItem))
        {
            return FormatFileMention(selectedItem!);
        }

        var activeDocument = _solutionContextService.GetActiveDocumentPath();
        if (string.IsNullOrWhiteSpace(activeDocument))
        {
            return string.Empty;
        }

        return FormatFileMention(_solutionContextService.FormatPathForPrompt(workingDirectory, activeDocument!));
    }

    private static string BuildFencedCode(string path, string content)
    {
        var language = GetFenceLanguage(path);
        var normalizedContent = (content ?? string.Empty).Trim();
        var longestBacktickRun = 0;
        var currentRun = 0;
        foreach (var character in normalizedContent)
        {
            currentRun = character == '`' ? currentRun + 1 : 0;
            longestBacktickRun = Math.Max(longestBacktickRun, currentRun);
        }

        var fence = new string('`', Math.Max(3, longestBacktickRun + 1));
        return fence + language + Environment.NewLine + normalizedContent + Environment.NewLine + fence;
    }

    private static string FormatFileMention(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        return normalized.Any(char.IsWhiteSpace)
            ? "@\"" + normalized.Replace("\"", "\\\"") + "\""
            : "@" + normalized;
    }

    private static string GetFenceLanguage(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "cs" => "csharp",
            "csproj" => "xml",
            "vb" => "vbnet",
            "js" => "javascript",
            "ts" => "typescript",
            "tsx" => "tsx",
            "jsx" => "jsx",
            "xaml" => "xml",
            "props" => "xml",
            "targets" => "xml",
            "json" => "json",
            "md" => "markdown",
            "ps1" => "powershell",
            "cmd" => "batch",
            "bat" => "batch",
            _ => extension
        };
    }

    private void ShowMessage(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "Codex",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
