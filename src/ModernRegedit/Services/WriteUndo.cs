using System.Globalization;

namespace ModernRegedit.Services;

public sealed record SubtreeSnapshot(
    string Path,
    (string Name, RegKind Kind, byte[] Data)[] Values,
    SubtreeSnapshot[] Children);

/// <summary>
/// 撤销支持：写前抓取受影响键快照，据此生成逆操作序列。
/// DeleteKey 的撤销依赖删除前的完整子树快照。
/// </summary>
public static class WriteUndo
{
    public static async Task<SubtreeSnapshot?> SnapshotSubtreeAsync(
        RegistryService service, string path, RegistryView view, bool deep)
    {
        (string, RegKind, byte[])[] values;
        SubtreeSnapshot[] children = [];

        bool exists;
        try
        {
            values = service.GetValues(path, view)
                .Select(v => (v.Name, v.Kind, v.Data))
                .ToArray();
            exists = true;
        }
        catch (IOException)
        {
            return null;
        }

        if (deep)
        {
            IReadOnlyList<RegistryKeyName> names = Array.Empty<RegistryKeyName>();
            try
            {
                names = service.GetSubKeyNames(path, view);
            }
            catch (IOException)
            {
            }

            var childTasks = names
                .Select(n => SnapshotSubtreeAsync(service, n.FullPath, view, deep: true))
                .ToArray();
            var done = await Task.WhenAll(childTasks);
            children = done.Where(s => s is not null).Select(s => s!).ToArray();
        }

        _ = exists;
        return new SubtreeSnapshot(path, values, children);
    }

    /// <summary>按涉及键生成快照请求集：删除/重命名的键抓完整子树，其余只抓本键值。</summary>
    public static async Task<Dictionary<string, SubtreeSnapshot?>> SnapshotForOpsAsync(
        RegistryService service, IReadOnlyList<WriteOperation> operations, RegistryView view)
    {
        var result = new Dictionary<string, SubtreeSnapshot?>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in operations.Select(o => o.KeyPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool deep = operations.Any(o =>
                string.Equals(o.KeyPath, path, StringComparison.OrdinalIgnoreCase)
                && o.Kind is WriteOpKind.DeleteKey or WriteOpKind.RenameKey);
            result[path] = await SnapshotSubtreeAsync(service, path, view, deep);
        }

        return result;
    }

    /// <summary>依据执行前快照生成逆操作序列（与 applied 顺序相反）。</summary>
    public static WriteOperation[] BuildUndoOps(
        IReadOnlyList<WriteOperation> applied,
        IReadOnlyDictionary<string, SubtreeSnapshot?> before)
    {
        var undo = new List<WriteOperation>();
        for (int i = applied.Count - 1; i >= 0; i--)
        {
            WriteOperation op = applied[i];
            before.TryGetValue(op.KeyPath, out SubtreeSnapshot? snapshot);
            switch (op.Kind)
            {
                case WriteOpKind.SetValue:
                    (string n, RegKind k, byte[] d)? old = snapshot?.Values
                        .FirstOrDefault(v => string.Equals(v.Name, op.Name, StringComparison.OrdinalIgnoreCase));
                    undo.Add(old is { } found
                        ? new WriteOperation(WriteOpKind.SetValue, op.KeyPath, found.n, ValueType: found.k, Data: found.d)
                        : new WriteOperation(WriteOpKind.DeleteValue, op.KeyPath, op.Name ?? string.Empty));
                    break;

                case WriteOpKind.DeleteValue:
                    old = snapshot?.Values
                        .FirstOrDefault(v => string.Equals(v.Name, op.Name, StringComparison.OrdinalIgnoreCase));
                    if (old is { } restored)
                    {
                        undo.Add(new WriteOperation(WriteOpKind.SetValue, op.KeyPath, restored.n, ValueType: restored.k, Data: restored.d));
                    }

                    break;

                case WriteOpKind.CreateKey:
                    undo.Add(new WriteOperation(WriteOpKind.DeleteKey, op.KeyPath));
                    break;

                case WriteOpKind.DeleteKey:
                    if (snapshot is not null)
                    {
                        undo.Add(new WriteOperation(WriteOpKind.CreateKey, op.KeyPath));
                        undo.AddRange(RebuildOps(snapshot));
                    }

                    break;

                case WriteOpKind.RenameKey:
                    string newPath = op.KeyPath[..op.KeyPath.LastIndexOf('\\')] + "\\" + op.NewName;
                    undo.Add(new WriteOperation(WriteOpKind.RenameKey, newPath, NewName: op.KeyPath[(op.KeyPath.LastIndexOf('\\') + 1)..]));
                    break;
            }
        }

        return undo.ToArray();
    }

    private static IEnumerable<WriteOperation> RebuildOps(SubtreeSnapshot snapshot)
    {
        // 无默认值的键不能补写默认（会多出 REG_NONE 条目），只在快照确有默认值时恢复。
        var defaultEntry = snapshot.Values.FirstOrDefault(v => v.Name.Length == 0);
        if (defaultEntry.Name is not null)
        {
            yield return new WriteOperation(WriteOpKind.SetValue, snapshot.Path,
                string.Empty,
                ValueType: defaultEntry.Kind,
                Data: defaultEntry.Data ?? Array.Empty<byte>());
        }

        foreach ((string name, RegKind kind, byte[] data) in snapshot.Values.Where(v => v.Name.Length > 0))
        {
            yield return new WriteOperation(WriteOpKind.SetValue, snapshot.Path, name, ValueType: kind, Data: data);
        }

        foreach (SubtreeSnapshot child in snapshot.Children)
        {
            yield return new WriteOperation(WriteOpKind.CreateKey, child.Path);
            foreach (WriteOperation op in RebuildOps(child))
            {
                yield return op;
            }
        }
    }

    public static string Describe(IReadOnlyList<WriteOperation> operations)
    {
        string first = operations.Count > 0 ? DescribeOp(operations[0]) : "change";
        return operations.Count > 1 ? $"{first} (+{operations.Count - 1} more)" : first;
    }

    private static string DescribeOp(WriteOperation op) => op.Kind switch
    {
        WriteOpKind.CreateKey => $"Create '{op.KeyPath}'",
        WriteOpKind.DeleteKey => $"Delete '{op.KeyPath}'",
        WriteOpKind.RenameKey => $"Rename '{op.KeyPath}'",
        WriteOpKind.SetValue => $"Set '{op.Name ?? "(Default)"}'",
        WriteOpKind.DeleteValue => $"Delete value '{op.Name}'",
        _ => "change",
    };
}
