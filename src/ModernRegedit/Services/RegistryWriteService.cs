using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace ModernRegedit.Services;

public enum WriteOpKind
{
    CreateKey,
    DeleteKey,
    RenameKey,
    SetValue,
    DeleteValue,
}

public sealed record WriteOperation(
    WriteOpKind Kind,
    [property: JsonPropertyName("key")] string KeyPath,
    string? Name = null,
    string? NewName = null,
    RegKind ValueType = RegKind.Sz,
    byte[]? Data = null);

public sealed record WriteBatch(RegistryView View, IReadOnlyList<WriteOperation> Operations);

public sealed record WriteResult(bool Ok, string Error = "", int FailedIndex = -1);

/// <summary>
/// 注册表写入编排：非提权打包进程把操作写入临时 JSON，经 AppExecutionAlias 以 UAC 提权
/// 二次自启执行（提权实例 MSIX 虚拟化禁用，写直达真实注册表）；提权或非打包进程直接执行。
/// </summary>
public static class RegistryWriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>打包进程的环境变量标记（MSIX 注入）。</summary>
    public static bool HasPackageIdentity { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PACKAGE_FAMILY_NAME"));

    /// <summary>命令行助手模式入口。args 形如 --apply &lt;命令.json&gt; &lt;结果.json&gt;。返回进程退出码。</summary>
    public static int RunApplyMode(IReadOnlyList<string> args)
    {
        WriteResult result;
        try
        {
            if (args.Count < 3)
            {
                result = new WriteResult(false, "Usage: --apply <commands.json> <result.json>");
            }
            else
            {
                var batch = JsonSerializer.Deserialize<WriteBatch>(File.ReadAllText(args[1]), JsonOptions);
                result = batch is null
                    ? new WriteResult(false, "Empty command file.")
                    : Execute(batch.Operations, batch.View);
            }
        }
        catch (Exception ex)
        {
            result = new WriteResult(false, ex.ToString());
        }

        try
        {
            if (args.Count >= 3)
            {
                File.WriteAllText(args[2], JsonSerializer.Serialize(result, JsonOptions));
            }
        }
        catch
        {
            // 结果文件写失败不影响退出码。
        }

        return result.Ok ? 0 : 1;
    }

    public static Task<WriteResult> ApplyAsync(IReadOnlyList<WriteOperation> operations, RegistryView view)
    {
        if (IsElevated || !HasPackageIdentity)
        {
            return Task.Run(() => Execute(operations, view));
        }

        return LaunchElevatedAsync(operations, view);
    }

    public static WriteResult Execute(IReadOnlyList<WriteOperation> operations, RegistryView view)
    {
        var service = new RegistryService();
        for (int i = 0; i < operations.Count; i++)
        {
            try
            {
                ExecuteOne(service, operations[i], view);
            }
            catch (Exception ex)
            {
                return new WriteResult(false, ex.Message, i);
            }
        }

        return new WriteResult(true);
    }

    private static void ExecuteOne(RegistryService service, WriteOperation op, RegistryView view)
    {
        switch (op.Kind)
        {
            case WriteOpKind.CreateKey:
                service.CreateKey(op.KeyPath, view);
                break;
            case WriteOpKind.DeleteKey:
                service.DeleteKey(op.KeyPath, view);
                break;
            case WriteOpKind.RenameKey:
                service.RenameKey(op.KeyPath, op.NewName ?? throw new IOException("Rename requires a new name."), view);
                break;
            case WriteOpKind.SetValue:
                service.SetValue(op.KeyPath, op.Name ?? string.Empty, op.ValueType, op.Data ?? Array.Empty<byte>(), view);
                break;
            case WriteOpKind.DeleteValue:
                service.DeleteValue(op.KeyPath, op.Name ?? throw new IOException("Delete value requires a name."), view);
                break;
            default:
                throw new IOException($"Unknown operation '{op.Kind}'.");
        }
    }

    private static async Task<WriteResult> LaunchElevatedAsync(IReadOnlyList<WriteOperation> operations, RegistryView view)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ModernRegedit");
        Directory.CreateDirectory(dir);
        string commandFile = Path.Combine(dir, $"write-{Guid.NewGuid():N}.json");
        string resultFile = Path.Combine(dir, $"write-{Guid.NewGuid():N}.result.json");
        await File.WriteAllTextAsync(commandFile, JsonSerializer.Serialize(new WriteBatch(view, operations), JsonOptions));

        string target = ResolveLaunchTarget();
        int rc = ShellNative.ShellExecuteRunAs(
            target, $"--apply \"{commandFile}\" \"{resultFile}\"", out IntPtr processHandle);
        if (rc != 0 || processHandle == IntPtr.Zero)
        {
            return new WriteResult(false, rc == 1223 ? "Elevation was cancelled." : $"Cannot launch elevated instance (error {rc}).");
        }

        ShellNative.WaitForExitAndClose(processHandle);

        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (File.Exists(resultFile))
            {
                string json = await File.ReadAllTextAsync(resultFile);
                try { File.Delete(resultFile); } catch { }
                return JsonSerializer.Deserialize<WriteResult>(json, JsonOptions) ?? new WriteResult(false, "Empty result.");
            }

            await Task.Delay(100);
        }

        try { File.Delete(commandFile); } catch { }
        return new WriteResult(false, "Elevated instance produced no result.");
    }

    private static string ResolveLaunchTarget()
    {
        string alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "ModernRegedit.exe");
        return File.Exists(alias) ? alias : "ModernRegedit.exe";
    }
}

internal static class ShellNative
{
    private const uint SeeMaskNocloseProcess = 0x00000040;
    private const uint SeeMaskNoAsync = 0x00001000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ShellExecuteExW(ref SHELLEXECUTEINFOW info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    internal static void WaitForExitAndClose(IntPtr processHandle)
    {
        WaitForSingleObject(processHandle, 0xFFFFFFFF);
        CloseHandle(processHandle);
    }

    /// <summary>以 runas 动词启动目标。返回 SHELLEXECUTEINFO.hInstApp 大于 32 表示成功。</summary>
    public static int ShellExecuteRunAs(string file, string parameters, out IntPtr processHandle)
    {
        var info = new SHELLEXECUTEINFOW
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
            fMask = SeeMaskNocloseProcess | SeeMaskNoAsync,
            lpVerb = "runas",
            lpFile = file,
            lpParameters = parameters,
            nShow = 0, // SW_HIDE
        };

        int rc = ShellExecuteExW(ref info);
        processHandle = info.hProcess;
        return rc != 0 ? 0 : info.hInstApp.ToInt32();
    }
}
