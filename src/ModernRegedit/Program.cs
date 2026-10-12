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
            // 与生成的 Main 一致：在 UI 线程上安装 DispatcherQueueSynchronizationContext，
            // 否则 await 续体回落线程池，UI 调用将 RPC_E_WRONG_THREAD 崩溃。
            var syncContext = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(syncContext);

            _ = new App();
        });

        return 0;
    }
}
