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
            (_, int values) = GetCounts(hKey);
            var result = new List<RegistryValueInfo>(values);
            var name = new char[256];

            for (int i = 0; i < values; i++)
            {
                int len = name.Length;
                int noDataLength = 0;
                int err = RegistryNative.RegEnumValue(
                    hKey, i, name, ref len, IntPtr.Zero, out int type, Array.Empty<byte>(), ref noDataLength);
                if (err != RegistryNative.ErrorSuccess && err != RegistryNative.ErrorMoreData)
                {
                    continue;
                }

                string valueName = new(name, 0, len);
                result.Add(new RegistryValueInfo(valueName, MapKind(type), QueryData(hKey, valueName)));
            }

            return result;
        }
        finally
        {
            RegistryNative.RegCloseKey(hKey);
        }
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

    private static (int SubKeyCount, int ValueCount) GetCounts(IntPtr hKey)
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

    private static byte[] QueryData(IntPtr hKey, string valueName)
    {
        int size = 0;
        int err = RegistryNative.RegQueryValueEx(hKey, valueName, IntPtr.Zero, out _, Array.Empty<byte>(), ref size);
        if (err != RegistryNative.ErrorSuccess && err != RegistryNative.ErrorMoreData)
        {
            throw new IOException($"Cannot query registry value '{valueName}'.");
        }

        var buffer = new byte[size];
        int length = buffer.Length;
        if (RegistryNative.RegQueryValueEx(hKey, valueName, IntPtr.Zero, out _, buffer, ref length)
            != RegistryNative.ErrorSuccess)
        {
            throw new IOException($"Cannot query registry value '{valueName}'.");
        }

        if (length != buffer.Length)
        {
            Array.Resize(ref buffer, length);
        }

        return buffer;
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
