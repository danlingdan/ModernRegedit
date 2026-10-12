using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RegistryService _service = new();

    private DispatcherQueue? _dispatcherQueue;

    private int _selectedSubKeyCount;

    private int _selectedValueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RegistryKeyNode? SelectedNode { get; set; }

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueRow> Values { get; } = new();

    public string StatusText =>
        SelectedNode is null
            ? "Ready"
            : $"{SelectedNode.FullPath} — {_selectedSubKeyCount} subkeys, {_selectedValueCount} values";

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        foreach (RegistryRoot root in RegistryService.Roots)
        {
            var node = new RegistryKeyNode(root.Name, root.Name);
            node.Children.Add(RegistryKeyNode.NewPlaceholder());
            Roots.Add(node);
        }
    }

    public void EnsureChildrenLoaded(RegistryKeyNode node)
    {
        if (node.ChildrenLoaded || node.IsPlaceholder)
        {
            return;
        }

        node.ChildrenLoaded = true;
        string fullPath = node.FullPath;

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
                node.Children.Clear();
                foreach (RegistryKeyName key in keys)
                {
                    var child = new RegistryKeyNode(key.Name, key.FullPath);
                    child.Children.Add(RegistryKeyNode.NewPlaceholder());
                    node.Children.Add(child);
                }
            });
        });
    }

    public void Select(RegistryKeyNode? node)
    {
        if (node is null || node.IsPlaceholder)
        {
            return;
        }

        SelectedNode = node;
        try
        {
            _selectedSubKeyCount = _service.GetSubKeyCount(node.FullPath);
        }
        catch (IOException)
        {
            _selectedSubKeyCount = 0;
        }

        Values.Clear();
        try
        {
            IReadOnlyList<RegistryValueInfo> values = _service.GetValues(node.FullPath);
            foreach (RegistryValueInfo value in values)
            {
                Values.Add(new RegistryValueRow
                {
                    Name = string.IsNullOrEmpty(value.Name) ? "(Default)" : value.Name,
                    KindDisplay = RegValueDecoder.KindName(value.Kind),
                    DataDisplay = RegValueDecoder.Decode(value.Kind, value.Data),
                });
            }

            _selectedValueCount = values.Count;
        }
        catch (IOException)
        {
            _selectedValueCount = 0;
        }

        OnPropertyChanged(nameof(StatusText));
    }
}
