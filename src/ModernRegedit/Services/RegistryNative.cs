using System.Runtime.InteropServices;

namespace ModernRegedit.Services;

internal static class RegistryNative
{
    public const int KeyRead = 0x00020019;
    public const int KeyWrite = 0x00020006;
    public const int KeyWow64_32Key = 0x0200;
    public const int KeyWow64_64Key = 0x0100;
    public const int Delete = 0x00010000;

    public const int ErrorSuccess = 0;
    public const int ErrorMoreData = 234;

    public const int RegOptionNonVolatile = 0;

    public static readonly IntPtr ClassesRoot = new(unchecked((int)0x80000000));
    public static readonly IntPtr CurrentUser = new(unchecked((int)0x80000001));
    public static readonly IntPtr LocalMachine = new(unchecked((int)0x80000002));
    public static readonly IntPtr Users = new(unchecked((int)0x80000003));
    public static readonly IntPtr CurrentConfig = new(unchecked((int)0x80000005));

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
    internal static extern int RegOpenKeyEx(IntPtr hKey, string? lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    internal static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", CharSet = CharSet.Unicode)]
    internal static extern int RegQueryInfoKey(
        IntPtr hKey,
        IntPtr lpClass,
        IntPtr lpcbClass,
        IntPtr lpReserved,
        out int lpcSubKeys,
        IntPtr lpcMaxSubKeyLen,
        IntPtr lpcMaxClassLen,
        out int lpcValues,
        IntPtr lpcMaxValueNameLen,
        IntPtr lpcMaxValueLen,
        IntPtr lpcbSecurityDescriptor,
        out long lpftLastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode)]
    internal static extern int RegEnumKeyEx(
        IntPtr hKey,
        int dwIndex,
        [Out] char[] lpName,
        ref int lpcchName,
        IntPtr lpReserved,
        IntPtr lpClass,
        IntPtr lpcbClass,
        out long lpftLastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumValueW", CharSet = CharSet.Unicode)]
    internal static extern int RegEnumValue(
        IntPtr hKey,
        int dwIndex,
        [Out] char[] lpValueName,
        ref int lpcchValueName,
        IntPtr lpReserved,
        out int lpType,
        [Out] byte[] lpData,
        ref int lpcbData);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
    internal static extern int RegQueryValueEx(
        IntPtr hKey,
        string? lpValueName,
        IntPtr lpReserved,
        out int lpType,
        [Out] byte[] lpData,
        ref int lpcbData);

    [DllImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", CharSet = CharSet.Unicode)]
    internal static extern int RegCreateKeyEx(
        IntPtr hKey,
        string? lpSubKey,
        uint Reserved,
        IntPtr lpClass,
        uint dwOptions,
        int samDesired,
        IntPtr lpSecurityAttributes,
        out IntPtr phkResult,
        out int lpdwDisposition);

    [DllImport("advapi32.dll", EntryPoint = "RegDeleteTreeW", CharSet = CharSet.Unicode)]
    internal static extern int RegDeleteTree(IntPtr hKey, string? lpSubKey);

    [DllImport("advapi32.dll", EntryPoint = "RegRenameKey", CharSet = CharSet.Unicode)]
    internal static extern int RegRenameKey(IntPtr hKey, string? lpSubKey, string? lpNewName);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode)]
    internal static extern int RegSetValueEx(
        IntPtr hKey,
        string? lpValueName,
        uint Reserved,
        int dwType,
        byte[] lpData,
        int cbData);

    [DllImport("advapi32.dll", EntryPoint = "RegDeleteValueW", CharSet = CharSet.Unicode)]
    internal static extern int RegDeleteValue(IntPtr hKey, string lpValueName);
}
