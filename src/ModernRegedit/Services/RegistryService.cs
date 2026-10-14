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

    public void CreateKey(string fullPath, RegistryView view = RegistryView.Default)
    {
        IntPtr root = OpenRoot(fullPath);
        string? subKey = SubKeyPath(fullPath);
        // RegCreateKeyEx 会连同缺失的中间键一起创建。
        int rc = RegistryNative.RegCreateKeyEx(
            root, subKey, 0, IntPtr.Zero, RegistryNative.RegOptionNonVolatile,
            RegistryNative.KeyWrite | WowFlags(view), IntPtr.Zero, out IntPtr hKey, out _);
        if (rc != RegistryNative.ErrorSuccess)
        {
            throw new IOException($"Cannot create key '{fullPath}'.", rc);
        }

        RegistryNative.RegCloseKey(hKey);
    }

    public void DeleteKey(string fullPath, RegistryView view = RegistryView.Default)
    {
        (IntPtr parent, string childName) = OpenParent(fullPath, view, RegistryNative.Delete | RegistryNative.KeyRead | RegistryNative.KeyWrite);
        try
        {
            int rc = RegistryNative.RegDeleteTree(parent, childName);
            if (rc != RegistryNative.ErrorSuccess)
            {
                throw new IOException($"Cannot delete key '{fullPath}'.", rc);
            }
        }
        finally
        {
            RegistryNative.RegCloseKey(parent);
        }
    }

    public void RenameKey(string fullPath, string newName, RegistryView view = RegistryView.Default)
    {
        (IntPtr parent, string childName) = OpenParent(fullPath, view, RegistryNative.KeyWrite);
        try
        {
            int rc = RegistryNative.RegRenameKey(parent, childName, newName);
            if (rc != RegistryNative.ErrorSuccess)
            {
                throw new IOException($"Cannot rename key '{fullPath}'.", rc);
            }
        }
        finally
        {
            RegistryNative.RegCloseKey(parent);
        }
    }

    public void SetValue(string fullPath, string valueName, RegKind kind, byte[] data, RegistryView view = RegistryView.Default)
    {
        IntPtr hKey = Open(fullPath, view, RegistryNative.KeyWrite);
        try
        {
            int rc = RegistryNative.RegSetValueEx(hKey, valueName, 0, (int)kind, data, data.Length);
            if (rc != RegistryNative.ErrorSuccess)
            {
                throw new IOException($"Cannot set value '{valueName}' under '{fullPath}'.", rc);
            }
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
    }

    public void DeleteValue(string fullPath, string valueName, RegistryView view = RegistryView.Default)
    {
        IntPtr hKey = Open(fullPath, view, RegistryNative.KeyWrite);
        try
        {
            int rc = RegistryNative.RegDeleteValue(hKey, valueName);
            if (rc != RegistryNative.ErrorSuccess)
            {
                throw new IOException($"Cannot delete value '{valueName}' under '{fullPath}'.", rc);
            }
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
    }

    private static (IntPtr Parent, string ChildName) OpenParent(string fullPath, RegistryView view, int access)
    {
        int separator = fullPath.LastIndexOf('\\');
        if (separator < 0)
        {
            throw new IOException($"Cannot modify registry root '{fullPath}'.");
        }

        IntPtr parent = Open(fullPath[..separator], view, access);
        return (parent, fullPath[(separator + 1)..]);
    }

    private static IntPtr Open(string fullPath, RegistryView view, int access)
    {
        IntPtr root = OpenRoot(fullPath);
        int sam = access | WowFlags(view);
        int error = RegistryNative.RegOpenKeyEx(root, SubKeyPath(fullPath), 0, sam, out IntPtr hKey);
        if (error != RegistryNative.ErrorSuccess)
        {
            throw new IOException($"Cannot open registry key '{fullPath}'.", new System.ComponentModel.Win32Exception(error));
        }

        return hKey;
    }

    private static IntPtr OpenRoot(string fullPath)
    {
        int separator = fullPath.IndexOf('\\');
        string rootName = separator < 0 ? fullPath : fullPath[..separator];
        return Roots.First(r => r.Name.Equals(rootName, StringComparison.OrdinalIgnoreCase)).Handle;
    }

    private static string? SubKeyPath(string fullPath)
    {
        int separator = fullPath.IndexOf('\\');
        return separator < 0 ? null : fullPath[(separator + 1)..];
    }

    internal static int WowFlags(RegistryView view) => view switch
    {
        RegistryView.Registry32 => RegistryNative.KeyWow64_32Key,
        RegistryView.Registry64 => RegistryNative.KeyWow64_64Key,
        _ => 0,
    };

    /// <summary>
    /// 枚举键下全部值。注意 RegEnumValueW 在数据缓冲不足（rc=234）时不保证回写名字长度，
    /// 必须按所需数据大小重枚举同一索引，成功返回后名字与数据才可靠。
    /// </summary>
    internal static IReadOnlyList<RegistryValueInfo> EnumerateValues(IntPtr hKey)
    {
        (_, int values) = GetCounts(hKey);
        var result = new List<RegistryValueInfo>(values);
        var name = new char[16384];
        var data = Array.Empty<byte>();

        for (int i = 0; i < values; i++)
        {
            int len = name.Length;
            int dataLen = 0;
            int err = RegistryNative.RegEnumValue(hKey, i, name, ref len, IntPtr.Zero, out int type, data, ref dataLen);
            while (err == RegistryNative.ErrorMoreData)
            {
                data = new byte[Math.Max(dataLen, Math.Max(256, data.Length * 2))];
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
        return Open(fullPath, view, RegistryNative.KeyRead);
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
