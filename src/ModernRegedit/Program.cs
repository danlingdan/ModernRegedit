using ModernRegedit.Services;

namespace ModernRegedit;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 提权助手模式：--apply <命令.json> <结果.json>。完全绕开 XAML，退出码即结果。
        if (args.Length >= 1 && args[0] == "--apply")
        {
            return RegistryWriteService.RunApplyMode(args);
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            _ = new App();
        });

        return 0;
    }
}
