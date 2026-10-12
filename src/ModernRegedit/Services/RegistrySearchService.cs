using System.Text;

namespace ModernRegedit.Services;

public sealed record SearchResult(string KeyPath, string ValueName, string DataPreview);

/// <summary>
/// 全库并行搜索：按根键分片，各分片在后台线程递归遍历，命中经 onBatch 小批回调（后台线程触发）。
/// 数据匹配只读每个值前 64KB；二进制按十六进制串匹配。
/// </summary>
public sealed class RegistrySearchService
{
    private const int BatchSize = 32;
    private const int MaxDecodedBytes = 64 * 1024;
    private const int PreviewLength = 200;

    public async Task SearchAsync(
        string query,
        IReadOnlyList<RegistryRoot> roots,
        RegistryView view,
        CancellationToken cancellation,
        Action<IReadOnlyList<SearchResult>> onBatch)
    {
        var shards = new Task[roots.Count];
        for (int i = 0; i < roots.Count; i++)
        {
            RegistryRoot root = roots[i];
            shards[i] = Task.Run(() => WalkShard(root, query, view, cancellation, onBatch), CancellationToken.None);
        }

        try
        {
            await Task.WhenAll(shards);
        }
        catch (OperationCanceledException)
        {
            // 用户取消属正常退出，分片内部已冲刷各自的批。
        }
    }

    private static void WalkShard(
        RegistryRoot root,
        string query,
        RegistryView view,
        CancellationToken cancellation,
        Action<IReadOnlyList<SearchResult>> onBatch)
    {
        int sam = BuildAccessMask(view);
        if (RegistryNative.RegOpenKeyEx(root.Handle, null, 0, sam, out IntPtr hKey) != RegistryNative.ErrorSuccess)
        {
            return;
        }

        var batch = new List<SearchResult>(BatchSize);
        var nameBuffer = new char[256];
        try
        {
            Walk(hKey, root.Name, query, sam, cancellation, onBatch, batch, nameBuffer);
        }
        catch (OperationCanceledException)
        {
            // 取消即停止本分片。
        }
        finally
        {
            if (batch.Count > 0)
            {
                onBatch(batch.ToArray());
            }

            RegistryNative.RegCloseKey(hKey);
        }
    }

    private static void Walk(
        IntPtr hKey,
        string path,
        string query,
        int sam,
        CancellationToken cancellation,
        Action<IReadOnlyList<SearchResult>> onBatch,
        List<SearchResult> batch,
        char[] nameBuffer)
    {
        cancellation.ThrowIfCancellationRequested();

        int subKeys;
        int valueCount;
        try
        {
            (subKeys, valueCount) = RegistryService.GetCounts(hKey);
        }
        catch (IOException)
        {
            return;
        }

        string keyName = path[(path.LastIndexOf('\\') + 1)..];
        if (keyName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            batch.Add(new SearchResult(path, string.Empty, "(key)"));
            FlushIfNeeded(batch, onBatch);
        }

        for (int i = 0; i < valueCount; i++)
        {
            int nameLength = nameBuffer.Length;
            int dataSize = 0;
            int err = RegistryNative.RegEnumValue(
                hKey, i, nameBuffer, ref nameLength, IntPtr.Zero, out int type, Array.Empty<byte>(), ref dataSize);
            if (err != RegistryNative.ErrorSuccess && err != RegistryNative.ErrorMoreData)
            {
                continue;
            }

            string valueName = new string(nameBuffer, 0, nameLength);

            byte[] data;
            try
            {
                data = RegistryService.QueryData(hKey, valueName);
            }
            catch (IOException)
            {
                continue;
            }

            if (data.Length > MaxDecodedBytes)
            {
                data = data[..MaxDecodedBytes];
            }

            var kind = MapKind(type);
            string decoded = RegValueDecoder.Decode(kind, data);
            string matchText = kind is RegKind.Binary or RegKind.None
                ? HexEncode(data)
                : decoded;

            bool nameMatch = valueName.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool dataMatch = matchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (nameMatch || dataMatch)
            {
                batch.Add(new SearchResult(path, valueName.Length == 0 ? "(Default)" : valueName, TruncatePreview(decoded)));
                FlushIfNeeded(batch, onBatch);
            }
        }

        for (int i = 0; i < subKeys; i++)
        {
            cancellation.ThrowIfCancellationRequested();

            int nameLength = nameBuffer.Length;
            if (RegistryNative.RegEnumKeyEx(hKey, i, nameBuffer, ref nameLength, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _)
                != RegistryNative.ErrorSuccess)
            {
                continue;
            }

            string childName = new string(nameBuffer, 0, nameLength);
            if (RegistryNative.RegOpenKeyEx(hKey, childName, 0, sam, out IntPtr child) != RegistryNative.ErrorSuccess)
            {
                continue;
            }

            try
            {
                Walk(child, $"{path}\\{childName}", query, sam, cancellation, onBatch, batch, nameBuffer);
            }
            finally
            {
                RegistryNative.RegCloseKey(child);
            }
        }
    }

    private static void FlushIfNeeded(List<SearchResult> batch, Action<IReadOnlyList<SearchResult>> onBatch)
    {
        if (batch.Count < BatchSize)
        {
            return;
        }

        // 传快照：调用方在 TryEnqueue 排队后立即清空复用列表，直接传引用会与 UI 回调迭代竞争。
        onBatch(batch.ToArray());
        batch.Clear();
    }

    private static int BuildAccessMask(RegistryView view)
    {
        int sam = RegistryNative.KeyRead;
        if (view == RegistryView.Registry32)
        {
            sam |= RegistryNative.KeyWow64_32Key;
        }

        if (view == RegistryView.Registry64)
        {
            sam |= RegistryNative.KeyWow64_64Key;
        }

        return sam;
    }

    private static string HexEncode(byte[] data)
    {
        var builder = new StringBuilder(data.Length * 3);
        foreach (byte b in data)
        {
            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }

    private static string TruncatePreview(string text)
        => text.Length <= PreviewLength ? text : text[..PreviewLength] + "…";

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
