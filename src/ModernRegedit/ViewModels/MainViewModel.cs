using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> RootAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCR"] = "HKEY_CLASSES_ROOT",
        ["HKCU"] = "HKEY_CURRENT_USER",
        ["HKLM"] = "HKEY_LOCAL_MACHINE",
        ["HKU"] = "HKEY_USERS",
        ["HKCC"] = "HKEY_CURRENT_CONFIG",
    };

    private readonly RegistryService _service = new();

    private readonly List<string> _history = new();

    private DispatcherQueue? _dispatcherQueue;

    /// <summary>选中代数：丢弃后台线程返回时已过期的值加载结果。</summary>
    private int _selectGeneration;

    /// <summary>历史游标；-1 表示无历史。</summary>
    private int _historyIndex = -1;

    private int _selectedSubKeyCount;

    private int _selectedValueCount;

    private bool _isLoadingValues;

    private string? _statusError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RegistryKeyNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string AddressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    public partial bool CanGoForward { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool Is32BitView { get; set; }

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueRow> Values { get; } = new();

    public string StatusText
    {
        get
        {
            if (_statusError is not null)
            {
                return _statusError;
            }

            if (SelectedNode is null)
            {
                return "Ready";
            }

            string view = Is32BitView ? "32-bit view" : "64-bit view";
            return _isLoadingValues
                ? $"{SelectedNode.FullPath} — loading…"
                : $"{SelectedNode.FullPath} — {_selectedSubKeyCount} subkeys, {_selectedValueCount} values — {view}";
        }
    }

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        foreach (RegistryRoot root in RegistryService.Roots)
        {
            Roots.Add(new RegistryKeyNode(root.Name, root.Name));
        }
    }

    private RegistryView CurrentView => Is32BitView ? RegistryView.Registry32 : RegistryView.Registry64;

    /// <summary>枚举子键（后台线程），完成后经 DispatcherQueue 应用。可并发调用，只枚举一次。</summary>
    public Task EnsureChildrenLoadedAsync(RegistryKeyNode node)
    {
        if (node.ChildrenLoaded)
        {
            return Task.CompletedTask;
        }

        if (node.LoadTask is not null)
        {
            return node.LoadTask;
        }

        string fullPath = node.FullPath;
        RegistryView view = CurrentView;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        node.LoadTask = completion.Task;

        Task.Run(() =>
        {
            IReadOnlyList<RegistryKeyName> keys;
            try
            {
                keys = _service.GetSubKeyNames(fullPath, view);
            }
            catch (IOException)
            {
                keys = Array.Empty<RegistryKeyName>();
            }

            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    foreach (RegistryKeyName key in keys)
                    {
                        node.Children.Add(new RegistryKeyNode(key.Name, key.FullPath));
                    }

                    node.ChildrenLoaded = true;
                    if (keys.Count == 0)
                    {
                        node.HasUnrealizedChildren = false;
                    }
                }
                finally
                {
                    completion.TrySetResult();
                }
            });
        });

        return completion.Task;
    }

    /// <summary>记录一次用户发起的位置变更；同一位置连续选中不重复入栈。</summary>
    public void PushHistory(string path)
    {
        if (_historyIndex >= 0 && string.Equals(_history[_historyIndex], path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(path);
        if (_history.Count > 500)
        {
            _history.RemoveAt(0);
        }

        _historyIndex = _history.Count - 1;
        UpdateNavigationState();
    }

    public async Task<RegistryKeyNode?> GoBackAsync()
    {
        if (!CanGoBack)
        {
            return null;
        }

        _historyIndex--;
        UpdateNavigationState();
        RegistryKeyNode? node = await ResolveNodeAsync(_history[_historyIndex]);
        if (node is null)
        {
            _historyIndex++;
            UpdateNavigationState();
        }

        return node;
    }

    public async Task<RegistryKeyNode?> GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return null;
        }

        _historyIndex++;
        UpdateNavigationState();
        RegistryKeyNode? node = await ResolveNodeAsync(_history[_historyIndex]);
        if (node is null)
        {
            _historyIndex--;
            UpdateNavigationState();
        }

        return node;
    }

    public async Task<RegistryKeyNode?> GoUpAsync()
    {
        string? current = SelectedNode?.FullPath;
        if (current is null)
        {
            return null;
        }

        int cut = current.LastIndexOf('\\');
        string parent = cut < 0 ? current : current[..cut];
        return await ResolveNodeAsync(parent);
    }

    private void UpdateNavigationState()
    {
        CanGoBack = _historyIndex > 0;
        CanGoForward = _historyIndex < _history.Count - 1;
    }

    /// <summary>按路径跳转：逐级展开并定位。成功返回目标节点（由窗口负责选中与滚动），失败返回 null 并在状态栏报错。</summary>
    public async Task<RegistryKeyNode?> NavigateToAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? normalized = NormalizePath(path);
        if (normalized is null)
        {
            _statusError = $"Cannot parse path '{path.Trim()}'";
            OnPropertyChanged(nameof(StatusText));
            return null;
        }

        RegistryKeyNode? node = await ResolveNodeAsync(normalized);
        if (node is null)
        {
            _statusError = $"Path not found: {normalized}";
            OnPropertyChanged(nameof(StatusText));
        }

        return node;
    }

    public void Select(RegistryKeyNode? node)
    {
        if (node is null)
        {
            return;
        }

        _statusError = null;
        SelectedNode = node;
        AddressText = node.FullPath;
        int generation = ++_selectGeneration;
        _selectedSubKeyCount = 0;
        _selectedValueCount = 0;
        _isLoadingValues = true;
        Values.Clear();
        OnPropertyChanged(nameof(StatusText));

        string fullPath = node.FullPath;
        RegistryView view = CurrentView;
        Task.Run(() =>
        {
            int subKeyCount = 0;
            try
            {
                subKeyCount = _service.GetSubKeyCount(fullPath, view);
            }
            catch (IOException)
            {
                // 无权限或键已消失：按 0 处理，不中断值加载。
            }

            var rows = new List<RegistryValueRow>();
            int valueCount = 0;
            try
            {
                IReadOnlyList<RegistryValueInfo> values = _service.GetValues(fullPath, view);
                valueCount = values.Count;

                RegistryValueInfo? defaultValue = values.FirstOrDefault(v => v.Name.Length == 0);
                rows.Add(new RegistryValueRow
                {
                    Name = "(Default)",
                    KindDisplay = defaultValue is null ? "REG_SZ" : RegValueDecoder.KindName(defaultValue.Kind),
                    DataDisplay = defaultValue is null ? "(value not set)" : RegValueDecoder.Decode(defaultValue.Kind, defaultValue.Data),
                });

                foreach (RegistryValueInfo value in values)
                {
                    if (value.Name.Length == 0)
                    {
                        continue;
                    }

                    rows.Add(new RegistryValueRow
                    {
                        Name = value.Name,
                        KindDisplay = RegValueDecoder.KindName(value.Kind),
                        DataDisplay = RegValueDecoder.Decode(value.Kind, value.Data),
                    });
                }
            }
            catch (IOException)
            {
                // 值读不到时至少保留 (Default) 占位行。
            }

            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (generation != _selectGeneration)
                {
                    return;
                }

                _selectedSubKeyCount = subKeyCount;
                _selectedValueCount = valueCount;
                _isLoadingValues = false;
                Values.Clear();
                foreach (RegistryValueRow row in rows)
                {
                    Values.Add(row);
                }

                OnPropertyChanged(nameof(StatusText));
            });
        });
    }

    /// <summary>切换 32/64 位视图：重建键树，原位置在新视图下重新定位（不改历史）。返回新视图下的原位置节点。</summary>
    public async Task<RegistryKeyNode?> SetViewAsync(bool is32Bit)
    {
        if (Is32BitView == is32Bit)
        {
            return SelectedNode;
        }

        Is32BitView = is32Bit;
        string? currentPath = SelectedNode?.FullPath;

        _selectGeneration++;
        Roots.Clear();
        foreach (RegistryRoot root in RegistryService.Roots)
        {
            Roots.Add(new RegistryKeyNode(root.Name, root.Name));
        }

        SelectedNode = null;
        _selectedSubKeyCount = 0;
        _selectedValueCount = 0;
        _isLoadingValues = false;
        _statusError = null;
        Values.Clear();
        OnPropertyChanged(nameof(StatusText));

        if (currentPath is null)
        {
            return null;
        }

        return await ResolveNodeAsync(currentPath);
    }

    private static string? NormalizePath(string input)
    {
        string path = input.Trim().Trim('"');
        if (path.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase))
        {
            path = path["Computer\\".Length..];
        }

        path = path.Replace('/', '\\');
        string[] segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        segments[0] = RootAliases.TryGetValue(segments[0], out string? alias) ? alias : segments[0];
        return RegistryService.Roots.Any(r => r.Name.Equals(segments[0], StringComparison.OrdinalIgnoreCase))
            ? string.Join('\\', segments)
            : null;
    }

    private async Task<RegistryKeyNode?> ResolveNodeAsync(string path)
    {
        string[] segments = path.Split('\\');
        RegistryKeyNode? node = Roots.FirstOrDefault(r => r.Title.Equals(segments[0], StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return null;
        }

        for (int i = 1; i < segments.Length; i++)
        {
            node.IsExpanded = true;
            await EnsureChildrenLoadedAsync(node);
            node = node.Children.FirstOrDefault(c => c.Title.Equals(segments[i], StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }
}
