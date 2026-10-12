using System.Text;

namespace ModernRegedit.Services;

public sealed record SearchResult(string KeyPath, string ValueName, string DataPreview);

/// <summary>搜索范围：键名 / 值名 / 值数据（至少勾选其一）。</summary>
public sealed record SearchOptions(bool MatchKeyNames, bool MatchValueNames, bool MatchValueData);

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
        SearchOptions options,
        RegistryView view,
        CancellationToken cancellation,
        Action<IReadOnlyList<SearchResult>> onBatch)
    {
        var shards = new Task[roots.Count];
        for (int i = 0; i < roots.Count; i++)
        {
            RegistryRoot root = roots[i];
            shards[i] = Task.Run(() => WalkShard(root, query, options, view, cancellation, onBatch), CancellationToken.None);
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
        SearchOptions options,
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
            Walk(hKey, root.Name, query, options, sam, cancellation, onBatch, batch, nameBuffer);
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
        SearchOptions options,
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

        if (options.MatchKeyNames)
        {
            string keyName = path[(path.LastIndexOf('\\') + 1)..];
            if (keyName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                batch.Add(new SearchResult(path, string.Empty, "(key)"));
                FlushIfNeeded(batch, onBatch);
            }
        }

        bool searchValues = options.MatchValueNames || options.MatchValueData;
        if (searchValues)
        {
            SearchValues(hKey, path, query, options, cancellation, onBatch, batch);
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
                Walk(child, $"{path}\\{childName}", query, options, sam, cancellation, onBatch, batch, nameBuffer);
            }
            finally
            {
                RegistryNative.RegCloseKey(child);
            }
        }
    }

    private static void SearchValues(
        IntPtr hKey,
        string path,
        string query,
        SearchOptions options,
        CancellationToken cancellation,
        Action<IReadOnlyList<SearchResult>> onBatch,
        List<SearchResult> batch)
    {
        IReadOnlyList<RegistryValueInfo> values;
        try
        {
            values = RegistryService.EnumerateValues(hKey);
        }
        catch (IOException)
        {
            return;
        }

        foreach (RegistryValueInfo value in values)
        {
            cancellation.ThrowIfCancellationRequested();

            string valueName = value.Name;
            if (options.MatchValueNames && valueName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                batch.Add(new SearchResult(path, valueName.Length == 0 ? "(Default)" : valueName, "(value name)"));
                FlushIfNeeded(batch, onBatch);
                continue;
            }

            if (!options.MatchValueData)
            {
                continue;
            }

            byte[] data = value.Data;
            if (data.Length > MaxDecodedBytes)
            {
                data = data[..MaxDecodedBytes];
            }

            string decoded = RegValueDecoder.Decode(value.Kind, data);
            string matchText = value.Kind is RegKind.Binary or RegKind.None
                ? HexEncode(data)
                : decoded;

            if (matchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                batch.Add(new SearchResult(path, valueName.Length == 0 ? "(Default)" : valueName, TruncatePreview(decoded)));
                FlushIfNeeded(batch, onBatch);
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
}
