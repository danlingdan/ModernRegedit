namespace ModernRegedit.Services;

public enum RegistryView
{
    Default,
    Registry32,
    Registry64,
}

public sealed record RegistryRoot(string Name, IntPtr Handle)
{
    public override string ToString() => Name;
}

public sealed record RegistryKeyName(string Name, string FullPath);

public enum RegKind
{
    None,
    Sz,
    ExpandSz,
    Binary,
    Dword,
    DwordBigEndian,
    Link,
    MultiSz,
    ResourceList,
    FullResourceDescriptor,
    ResourceRequirementsList,
    Qword,
}

public sealed record RegistryValueInfo(string Name, RegKind Kind, byte[] Data);

public sealed class RegistryService
{
    public static readonly IReadOnlyList<RegistryRoot> Roots = new[]
    {
        new RegistryRoot("HKEY_CLASSES_ROOT", RegistryNative.ClassesRoot),
        new RegistryRoot("HKEY_CURRENT_USER", RegistryNative.CurrentUser),
        new RegistryRoot("HKEY_LOCAL_MACHINE", RegistryNative.LocalMachine),
        new RegistryRoot("HKEY_USERS", RegistryNative.Users),
        new RegistryRoot("HKEY_CURRENT_CONFIG", RegistryNative.CurrentConfig),
    };

    public int GetSubKeyCount(string fullPath, RegistryView view = RegistryView.Default)
    {
        IntPtr hKey = Open(fullPath, view);
        try
        {
            return GetCounts(hKey).SubKeyCount;
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
    }

    public IReadOnlyList<RegistryKeyName> GetSubKeyNames(string fullPath, RegistryView view = RegistryView.Default)
    {
        IntPtr hKey = Open(fullPath, view);
        try
        {
            (int subKeys, _) = GetCounts(hKey);
            var result = new List<RegistryKeyName>(subKeys);
            var name = new char[256];

            for (int i = 0; i < subKeys; i++)
            {
                int len = name.Length;
                if (RegistryNative.RegEnumKeyEx(hKey, i, name, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _)
                    != RegistryNative.ErrorSuccess)
                {
                    continue;
                }

                string keyName = new(name, 0, len);
                result.Add(new RegistryKeyName(keyName, $"{fullPath}\\{keyName}"));
            }

            return result;
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
    }

    public IReadOnlyList<RegistryValueInfo> GetValues(string fullPath, RegistryView view = RegistryView.Default)
    {
        IntPtr hKey = Open(fullPath, view);
        try
        {
            return EnumerateValues(hKey);
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// 枚举键下全部值。注意 RegEnumValueW 在数据缓冲不足（rc=234）时不保证回写名字长度，
    /// 必须按所需数据大小重枚举同一索引，成功返回后名字与数据才可靠。
    /// </summary>
    internal static IReadOnlyList<RegistryValueInfo> EnumerateValues(IntPtr hKey)
    {
        (_, int values) = GetCounts(hKey);
        var result = new List<RegistryValueInfo>(values);
        var name = new char[256];
        var data = Array.Empty<byte>();

        for (int i = 0; i < values; i++)
        {
            int len = name.Length;
            int dataLen = 0;
            int err = RegistryNative.RegEnumValue(hKey, i, name, ref len, IntPtr.Zero, out int type, data, ref dataLen);
            if (err == RegistryNative.ErrorMoreData)
            {
                data = new byte[dataLen];
                dataLen = data.Length;
                len = name.Length;
                err = RegistryNative.RegEnumValue(hKey, i, name, ref len, IntPtr.Zero, out type, data, ref dataLen);
            }

            if (err != RegistryNative.ErrorSuccess)
            {
                continue;
            }

            string valueName = new(name, 0, len);
            var payload = new byte[dataLen];
            Array.Copy(data, payload, dataLen);
            result.Add(new RegistryValueInfo(valueName, MapKind(type), payload));
        }

        return result;
    }

    private static IntPtr Open(string fullPath, RegistryView view)
    {
        int separator = fullPath.IndexOf('\\');
        string rootName = separator < 0 ? fullPath : fullPath[..separator];
        string? subKey = separator < 0 ? null : fullPath[(separator + 1)..];

        IntPtr root = Roots.First(r => r.Name.Equals(rootName, StringComparison.OrdinalIgnoreCase)).Handle;

        int sam = RegistryNative.KeyRead;
        if (view == RegistryView.Registry32)
        {
            sam |= RegistryNative.KeyWow64_32Key;
        }

        if (view == RegistryView.Registry64)
        {
            sam |= RegistryNative.KeyWow64_64Key;
        }

        if (RegistryNative.RegOpenKeyEx(root, subKey, 0, sam, out IntPtr hKey) != RegistryNative.ErrorSuccess)
        {
            throw new IOException($"Cannot open registry key '{fullPath}'.");
        }

        return hKey;
    }

    internal static (int SubKeyCount, int ValueCount) GetCounts(IntPtr hKey)
    {
        if (RegistryNative.RegQueryInfoKey(
                hKey,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out int subKeys, IntPtr.Zero, IntPtr.Zero,
                out int values, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out _)
            != RegistryNative.ErrorSuccess)
        {
            throw new IOException("Cannot query registry key info.");
        }

        return (subKeys, values);
    }

    private static RegKind MapKind(int type) => type switch
    {
        1 => RegKind.Sz,
        2 => RegKind.ExpandSz,
        3 => RegKind.Binary,
        4 => RegKind.Dword,
        5 => RegKind.DwordBigEndian,
        6 => RegKind.Link,
        7 => RegKind.MultiSz,
        8 => RegKind.ResourceList,
        9 => RegKind.FullResourceDescriptor,
        10 => RegKind.ResourceRequirementsList,
        11 => RegKind.Qword,
        _ => RegKind.None,
    };
}
