using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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

    private readonly RegistrySearchService _searchService = new();

    private readonly List<string> _history = new();

    private CancellationTokenSource? _searchCts;

    /// <summary>输入防抖代数：只执行最后一次输入触发的搜索。</summary>
    private int _searchDebounceGeneration;

    private int _searchResultCount;

    private bool _searchCapped;

    private Stopwatch? _searchStopwatch;

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

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CloseButtonVisibility))]
    public partial bool IsSearchRunning { get; private set; }

    [ObservableProperty]
    public partial string SearchStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MatchKeyNames { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchValueNames { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchValueData { get; set; } = true;

    partial void OnMatchKeyNamesChanged(bool value) => RestartSearchIfActive();

    partial void OnMatchValueNamesChanged(bool value) => RestartSearchIfActive();

    partial void OnMatchValueDataChanged(bool value) => RestartSearchIfActive();

    /// <summary>搜索进行中或已有结果时，范围变化立即重跑当前查询。</summary>
    private void RestartSearchIfActive()
    {
        if (!string.IsNullOrWhiteSpace(SearchText) && _dispatcherQueue is not null)
        {
            _searchDebounceGeneration++;
            _ = StartSearchAsync();
        }
    }

    [ObservableProperty]
    public partial Visibility SearchPanelVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>跳转命中的值名（空串 = 默认值，null = 仅键命中）；值列表加载完成后选中该行。</summary>
    public string? PendingJumpValueName { get; set; }

    [ObservableProperty]
    public partial RegistryValueRow? SelectedValueRow { get; set; }

    public Visibility CancelButtonVisibility => IsSearchRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CloseButtonVisibility => IsSearchRunning ? Visibility.Collapsed : Visibility.Visible;

    public ObservableCollection<SearchResultRow> SearchResults { get; } = new();

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

    public ObservableCollection<string> Favorites { get; } = new();

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        foreach (RegistryRoot root in RegistryService.Roots)
        {
            Roots.Add(new RegistryKeyNode(root.Name, root.Name));
        }

        _settings = SettingsStore.Load();
        foreach (string favorite in _settings.Favorites)
        {
            Favorites.Add(favorite);
        }

        if (_settings.Is32BitView && Environment.Is64BitOperatingSystem)
        {
            Is32BitView = true;
        }
    }

    private SettingsStore _settings = new();

    public string? LastKey => _settings.LastKey;

    public int ThemeSetting => _settings.Theme;

    public int BackupRetentionSetting => _settings.BackupRetention;

    public void UpdateSettings(int theme, int backupRetention)
    {
        _settings.Theme = theme;
        _settings.BackupRetention = backupRetention;
        SaveSettings();
    }

    public void SaveSettings()
    {
        _settings.Favorites = Favorites.ToList();
        _settings.Is32BitView = Is32BitView;
        _settings.LastKey = SelectedNode?.FullPath;
        _settings.Save();
    }

    public bool IsFavorite(string path) =>
        Favorites.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));

    public void ToggleFavorite(string path)
    {
        string? existing = Favorites.FirstOrDefault(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Favorites.Remove(existing);
        }
        else
        {
            Favorites.Add(path);
        }

        SaveSettings();
    }

    /// <summary>窗口关闭前保存位置记忆。</summary>
    public void PersistSession()
    {
        SaveSettings();
    }

    public RegistryView CurrentView => Is32BitView ? RegistryView.Registry32 : RegistryView.Registry64;

    /// <summary>搜索结果上限：全库超宽匹配时保护界面。</summary>
    private const int MaxSearchResults = 10000;

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceGeneration++;
        int generation = _searchDebounceGeneration;

        if (string.IsNullOrWhiteSpace(value))
        {
            _searchCts?.Cancel();
            CloseSearchPanel();
            return;
        }

        _ = DebounceThenSearchAsync(generation);
    }

    private async Task DebounceThenSearchAsync(int generation)
    {
        await Task.Delay(250);
        if (generation != _searchDebounceGeneration || _dispatcherQueue is null)
        {
            return;
        }

        await StartSearchAsync();
    }

    /// <summary>立即搜索（回车触发，跳过防抖等待）。</summary>
    public async Task SearchNowAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            _searchCts?.Cancel();
            CloseSearchPanel();
            return;
        }

        _searchDebounceGeneration++;
        await StartSearchAsync();
    }

    public async Task StartSearchAsync()
    {
        string query = SearchText.Trim();
        _searchCts?.Cancel();
        if (query.Length == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        SearchResults.Clear();
        _searchResultCount = 0;
        _searchCapped = false;
        _searchStopwatch = Stopwatch.StartNew();
        IsSearchRunning = true;
        SearchPanelVisibility = Visibility.Visible;

        if (!MatchKeyNames && !MatchValueNames && !MatchValueData)
        {
            IsSearchRunning = false;
            SearchStatus = "Select at least one search scope";
            return;
        }

        SearchStatus = "Searching…";

        await _searchService.SearchAsync(
            query,
            RegistryService.Roots,
            new SearchOptions(MatchKeyNames, MatchValueNames, MatchValueData),
            CurrentView,
            cts.Token,
            batch =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    if (_searchCts != cts)
                    {
                        return;
                    }

                    foreach (SearchResult result in batch)
                    {
                        if (_searchResultCount >= MaxSearchResults)
                        {
                            break;
                        }

                        _searchResultCount++;
                        SearchResults.Add(new SearchResultRow
                        {
                            KeyPath = result.KeyPath,
                            ValueName = result.ValueName,
                            DataPreview = result.DataPreview,
                        });
                    }

                    if (_searchResultCount >= MaxSearchResults)
                    {
                        _searchCapped = true;
                        cts.Cancel();
                    }

                    SearchStatus = _searchCapped
                        ? $"First {MaxSearchResults} results — refine the query"
                        : $"Searching… {_searchResultCount} results";
                });
            });

        if (_searchCts != cts)
        {
            return; // 已被新搜索取代
        }

        IsSearchRunning = false;
        string elapsed = _searchStopwatch!.Elapsed.TotalSeconds.ToString("0.0");
        SearchStatus = _searchCapped
            ? $"First {MaxSearchResults} results ({elapsed}s) — refine the query"
            : cts.IsCancellationRequested
                ? $"Cancelled — {_searchResultCount} results ({elapsed}s)"
                : $"{_searchResultCount} results ({elapsed}s)";
    }

    public void CancelSearch()
    {
        _searchCts?.Cancel();
    }

    public void CloseSearchPanel()
    {
        _searchCts?.Cancel();
        SearchResults.Clear();
        _searchResultCount = 0;
        IsSearchRunning = false;
        SearchPanelVisibility = Visibility.Collapsed;
        SearchStatus = string.Empty;
    }

    private sealed record HistoryEntry(string Description, WriteOperation[] UndoOps, WriteOperation[] ForwardOps);

    private readonly Stack<HistoryEntry> _undoStack = new();
    private readonly Stack<HistoryEntry> _redoStack = new();

    private const int MaxUndoLevels = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool CanRedo { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    private void UpdateUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        CanRedo = _redoStack.Count > 0;
    }

    public async Task<bool> UndoAsync()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        HistoryEntry entry = _undoStack.Pop();
        UpdateUndoState();
        Dictionary<string, SubtreeSnapshot?> before =
            await WriteUndo.SnapshotForOpsAsync(new RegistryService(), entry.UndoOps, CurrentView);
        if (await ExecuteWithHistoryAsync(entry.UndoOps, before, refreshPerPath: true, recordUndo: false))
        {
            _redoStack.Push(entry);
            UpdateUndoState();
            SetStatusMessage($"Undo: {entry.Description}");
            return true;
        }

        _undoStack.Push(entry);
        UpdateUndoState();
        return false;
    }

    public async Task<bool> RedoAsync()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        HistoryEntry entry = _redoStack.Pop();
        UpdateUndoState();
        Dictionary<string, SubtreeSnapshot?> before =
            await WriteUndo.SnapshotForOpsAsync(new RegistryService(), entry.ForwardOps, CurrentView);
        if (await ExecuteWithHistoryAsync(entry.ForwardOps, before, refreshPerPath: true, recordUndo: false))
        {
            _undoStack.Push(entry);
            UpdateUndoState();
            SetStatusMessage($"Redo: {entry.Description}");
            return true;
        }

        _redoStack.Push(entry);
        UpdateUndoState();
        return false;
    }

    /// <summary>执行一批写操作，成功后刷新受影响的键；失败把原因写入状态栏。</summary>
    public async Task<bool> ApplyWritesAsync(IReadOnlyList<WriteOperation> operations, bool refreshPerPath = true)
    {
        Dictionary<string, SubtreeSnapshot?> before =
            await WriteUndo.SnapshotForOpsAsync(new RegistryService(), operations, CurrentView);
        return await ExecuteWithHistoryAsync(operations, before, refreshPerPath, recordUndo: true);
    }

    private async Task<bool> ExecuteWithHistoryAsync(
        IReadOnlyList<WriteOperation> operations,
        IReadOnlyDictionary<string, SubtreeSnapshot?> before,
        bool refreshPerPath,
        bool recordUndo)
    {
        WriteResult result = await RegistryWriteService.ApplyAsync(operations, CurrentView);
        if (!result.Ok)
        {
            _statusError = $"Write failed: {result.Error}";
            OnPropertyChanged(nameof(StatusText));
            return false;
        }

        if (recordUndo)
        {
            WriteOperation[] undoOps = WriteUndo.BuildUndoOps(operations, before);
            if (undoOps.Length > 0)
            {
                _undoStack.Push(new HistoryEntry(WriteUndo.Describe(operations), undoOps, operations.ToArray()));
                while (_undoStack.Count > MaxUndoLevels)
                {
                    _undoStack.Pop();
                }

                _redoStack.Clear();
                UpdateUndoState();
            }
        }

        if (refreshPerPath)
        {
            foreach (string path in operations.Select(o => o.KeyPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await RefreshSubtreeAsync(path);
            }
        }
        else
        {
            foreach (string rootName in operations.Select(o => RootNameOf(o.KeyPath)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ResetRoot(rootName);
            }
        }

        if (SelectedNode is not null)
        {
            RegistryKeyNode? again = await ResolveNodeAsync(SelectedNode.FullPath);
            if (again is not null)
            {
                Select(again);
            }
            else
            {
                // 当前选中键已被删除/改名：清空值面板，等待窗口层改选。
                SelectedNode = null;
                Values.Clear();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        return true;
    }

    public void SetStatusMessage(string message)
    {
        _statusError = message;
        OnPropertyChanged(nameof(StatusText));
    }

    private static string RootNameOf(string path)
    {
        int separator = path.IndexOf('\\');
        return separator < 0 ? path : path[..separator];
    }

    /// <summary>导入后整根重置（子键全部收起），下次展开时按新状态重建。</summary>
    private void ResetRoot(string rootName)
    {
        RegistryKeyNode? root = Roots.FirstOrDefault(r => string.Equals(r.Title, rootName, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        root.Children.Clear();
        root.ChildrenLoaded = false;
        root.LoadTask = null;
        root.HasUnrealizedChildren = true;
        root.IsExpanded = false;
    }

    /// <summary>重建受影响键父节点的子键列表（含增删改名后的名字变化），保持展开状态。</summary>
    private async Task RefreshSubtreeAsync(string path)
    {
        int separator = path.LastIndexOf('\\');
        string parentPath = separator > 0 ? path[..separator] : path;
        RegistryKeyNode? parent = await ResolveNodeAsync(parentPath);
        if (parent is null)
        {
            return;
        }

        // 未展开的节点：标脏即可，下次展开按新状态加载。
        if (!parent.IsExpanded || !parent.ChildrenLoaded)
        {
            parent.ChildrenLoaded = false;
            parent.LoadTask = null;
            parent.HasUnrealizedChildren = true;
            if (parent.IsExpanded)
            {
                await EnsureChildrenLoadedAsync(parent);
            }

            return;
        }

        // 已展开：差量更新。对展开中的节点做 Children.Clear() 全量重建，
        // 会在 TreeView 虚拟化下触发 WinUI 内部访问冲突（#8275 家族）。
        RegistryView view = CurrentView;
        IReadOnlyList<RegistryKeyName>? fresh = await Task.Run(() =>
        {
            try
            {
                return _service.GetSubKeyNames(parent.FullPath, view);
            }
            catch (IOException)
            {
                return null;
            }
        });
        if (fresh is null)
        {
            return;
        }

        var byTitle = new Dictionary<string, RegistryKeyNode>(StringComparer.OrdinalIgnoreCase);
        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            byTitle[parent.Children[i].Title] = parent.Children[i];
        }

        var freshTitles = fresh.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            if (!freshTitles.Contains(parent.Children[i].Title))
            {
                parent.Children.RemoveAt(i);
            }
        }

        int cursor = 0;
        foreach (RegistryKeyName key in fresh)
        {
            if (!byTitle.TryGetValue(key.Name, out RegistryKeyNode? node))
            {
                node = new RegistryKeyNode(key.Name, key.FullPath);
            }

            if (cursor < parent.Children.Count)
            {
                if (!string.Equals(parent.Children[cursor].Title, key.Name, StringComparison.OrdinalIgnoreCase))
                {
                    parent.Children.Remove(node);
                    parent.Children.Insert(cursor, node);
                }
            }
            else
            {
                parent.Children.Insert(cursor, node);
            }

            cursor++;
        }
    }

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

            var decoded = new List<(string Name, RegKind Kind, string Data, byte[] Raw)>();
            int valueCount = 0;
            try
            {
                IReadOnlyList<RegistryValueInfo> values = _service.GetValues(fullPath, view);
                valueCount = values.Count;

                foreach (RegistryValueInfo value in values)
                {
                    decoded.Add((value.Name, value.Kind, RegValueDecoder.Decode(value.Kind, value.Data), value.Data));
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
                SelectedValueRow = null;

                // 图标与画刷取自应用资源，只能在 UI 线程解析。
                int defaultIndex = decoded.FindIndex(v => v.Name.Length == 0);
                if (defaultIndex >= 0)
                {
                    (string name, RegKind kind, string data, byte[] raw) = decoded[defaultIndex];
                    Values.Add(MakeRow("(Default)", name, kind, raw, data));
                }
                else
                {
                    Values.Add(MakeRow("(Default)", string.Empty, RegKind.Sz, Array.Empty<byte>(), "(value not set)", isUnset: true));
                }

                foreach ((string name, RegKind kind, string data, byte[] raw) in decoded)
                {
                    if (name.Length > 0)
                    {
                        Values.Add(MakeRow(name, name, kind, raw, data));
                    }
                }

                // 搜索结果跳转：选中命中的值行
                string? pending = PendingJumpValueName;
                PendingJumpValueName = null;
                if (pending is not null)
                {
                    RegistryValueRow? target = Values.FirstOrDefault(v =>
                        pending.Length == 0 ? v.Name == "(Default)" : string.Equals(v.Name, pending, StringComparison.OrdinalIgnoreCase));
                    if (target is not null)
                    {
                        SelectedValueRow = target;
                    }
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

    private static RegistryValueRow MakeRow(
        string displayName, string valueName, RegKind kind, byte[] data, string dataDisplay, bool isUnset = false) => new()
    {
        Name = displayName,
        ValueName = valueName,
        Kind = kind,
        Data = data,
        IsUnset = isUnset,
        KindDisplay = RegValueDecoder.KindName(kind),
        DataDisplay = dataDisplay,
    };

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
