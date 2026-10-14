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

        try
        {
            values = service.GetValues(path, view)
                .Select(v => (v.Name, v.Kind, v.Data))
                .ToArray();
        }
        catch (IOException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 })
        {
            return null;
        }

        if (deep)
        {
            IReadOnlyList<RegistryKeyName> names = service.GetSubKeyNames(path, view);

            var childTasks = names
                .Select(n => SnapshotSubtreeAsync(service, n.FullPath, view, deep: true))
                .ToArray();
            var done = await Task.WhenAll(childTasks);
            children = done.Where(s => s is not null).Select(s => s!).ToArray();
        }

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
        var state = new Dictionary<string, SubtreeSnapshot?>(StringComparer.OrdinalIgnoreCase);
        void Remember(SubtreeSnapshot snapshot)
        {
            state[snapshot.Path] = snapshot;
            foreach (var child in snapshot.Children) Remember(child);
        }
        foreach (var (path, snapshot) in before)
        {
            if (snapshot is null) state.TryAdd(path, null);
            else Remember(snapshot);
        }
        SubtreeSnapshot Capture(string path)
        {
            var snapshot = state[path]!;
            var children = state.Where(p => p.Value is not null && p.Key.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)
                && !p.Key[(path.Length + 1)..].Contains('\\')).Select(p => Capture(p.Key)).ToArray();
            return snapshot with { Children = children };
        }
        var blocks = new List<WriteOperation[]>();
        foreach (var op in applied)
        {
            state.TryGetValue(op.KeyPath, out var snapshot);
            var inverse = new List<WriteOperation>();
            switch (op.Kind)
            {
                case WriteOpKind.SetValue:
                case WriteOpKind.DeleteValue:
                    var old = snapshot?.Values.Where(v => string.Equals(v.Name, op.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        .Select(v => ((string Name, RegKind Kind, byte[] Data)?)v).FirstOrDefault();
                    if (old is { } value)
                        inverse.Add(new(WriteOpKind.SetValue, op.KeyPath, value.Name, ValueType: value.Kind, Data: value.Data));
                    else if (op.Kind == WriteOpKind.SetValue)
                        inverse.Add(new(WriteOpKind.DeleteValue, op.KeyPath, op.Name ?? string.Empty));
                    if (snapshot is not null)
                    {
                        var values = snapshot.Values.Where(v => !string.Equals(v.Name, op.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (op.Kind == WriteOpKind.SetValue) values.Add((op.Name ?? string.Empty, op.ValueType, op.Data ?? []));
                        state[op.KeyPath] = snapshot with { Values = values.ToArray() };
                    }
                    break;
                case WriteOpKind.CreateKey:
                    if (snapshot is null)
                    {
                        inverse.Add(new(WriteOpKind.DeleteKey, op.KeyPath));
                        state[op.KeyPath] = new(op.KeyPath, [], []);
                    }
                    break;
                case WriteOpKind.DeleteKey:
                    if (snapshot is not null)
                    {
                        inverse.Add(new(WriteOpKind.CreateKey, op.KeyPath));
                        inverse.AddRange(RebuildOps(Capture(op.KeyPath)));
                    }
                    foreach (string path in state.Keys.Where(p => p.Equals(op.KeyPath, StringComparison.OrdinalIgnoreCase)
                        || p.StartsWith(op.KeyPath + "\\", StringComparison.OrdinalIgnoreCase)).ToArray()) state[path] = null;
                    break;
                case WriteOpKind.RenameKey:
                    string parent = op.KeyPath[..op.KeyPath.LastIndexOf('\\')];
                    string newPath = parent + "\\" + op.NewName;
                    inverse.Add(new(WriteOpKind.RenameKey, newPath, NewName: op.KeyPath[(parent.Length + 1)..]));
                    foreach (string path in state.Keys.Where(p => p.Equals(op.KeyPath, StringComparison.OrdinalIgnoreCase)
                        || p.StartsWith(op.KeyPath + "\\", StringComparison.OrdinalIgnoreCase)).ToArray())
                    {
                        if (state[path] is { } moved) state[newPath + path[op.KeyPath.Length..]] = moved with { Path = newPath + path[op.KeyPath.Length..] };
                        state[path] = null;
                    }
                    break;
            }
            blocks.Add(inverse.ToArray());
        }
        return blocks.AsEnumerable().Reverse().SelectMany(block => block).ToArray();
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
