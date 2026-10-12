using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RegistryService _service = new();

    private DispatcherQueue? _dispatcherQueue;

    /// <summary>选中代数：丢弃后台线程返回时已过期的值加载结果。</summary>
    private int _selectGeneration;

    private int _selectedSubKeyCount;

    private int _selectedValueCount;

    private bool _isLoadingValues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RegistryKeyNode? SelectedNode { get; set; }

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueRow> Values { get; } = new();

    public string StatusText
    {
        get
        {
            if (SelectedNode is null)
            {
                return "Ready";
            }

            string view = Environment.Is64BitOperatingSystem ? "64-bit view" : "32-bit view";
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
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        node.LoadTask = completion.Task;

        Task.Run(() =>
        {
            IReadOnlyList<RegistryKeyName> keys;
            try
            {
                keys = _service.GetSubKeyNames(fullPath);
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

    public void Select(RegistryKeyNode? node)
    {
        if (node is null)
        {
            return;
        }

        SelectedNode = node;
        int generation = ++_selectGeneration;
        _selectedSubKeyCount = 0;
        _selectedValueCount = 0;
        _isLoadingValues = true;
        Values.Clear();
        OnPropertyChanged(nameof(StatusText));

        string fullPath = node.FullPath;
        Task.Run(() =>
        {
            int subKeyCount = 0;
            try
            {
                subKeyCount = _service.GetSubKeyCount(fullPath);
            }
            catch (IOException)
            {
                // 无权限或键已消失：按 0 处理，不中断值加载。
            }

            var rows = new List<RegistryValueRow>();
            int valueCount = 0;
            try
            {
                IReadOnlyList<RegistryValueInfo> values = _service.GetValues(fullPath);
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
}
