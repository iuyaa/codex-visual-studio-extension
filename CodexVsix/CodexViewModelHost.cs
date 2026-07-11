using Microsoft.VisualStudio.Shell;
using CodexVsix.ViewModels;

namespace CodexVsix;

internal static class CodexViewModelHost
{
    private static CodexToolWindowViewModel? _instance;

    public static CodexToolWindowViewModel GetOrCreate()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return _instance ??= new CodexToolWindowViewModel();
    }

    public static void Dispose()
    {
        var instance = _instance;
        _instance = null;
        instance?.Dispose();
    }
}
