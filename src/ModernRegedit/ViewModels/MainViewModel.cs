using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RegistryService _service = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RegistryKeyNode? SelectedNode { get; set; }

    private int _selectedSubKeyCount;

    private int _selectedValueCount;

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueRow> Values { get; } = new();

    public string StatusText =>
        SelectedNode is null
            ? "Ready"
            : $"{SelectedNode.FullPath} — {_selectedSubKeyCount} subkeys, {_selectedValueCount} values";

    public void Initialize()
    {
        foreach (RegistryRoot root in RegistryService.Roots)
        {
            int count = _service.GetSubKeyCount(root.Name);
            var node = new RegistryKeyNode(root.Name, root.Name, hasChildren: count > 0);
            if (count > 0)
            {
                node.Children.Add(RegistryKeyNode.Placeholder);
            }

            Roots.Add(node);
        }
    }

    public void EnsureChildrenLoaded(RegistryKeyNode node)
    {
        if (node.ChildrenLoaded || node.IsPlaceholder || !node.HasChildren)
        {
            return;
        }

        node.Children.Clear();
        foreach (RegistryKeyName key in _service.GetSubKeyNames(node.FullPath))
        {
            int count;
            try
            {
                count = _service.GetSubKeyCount(key.FullPath);
            }
            catch (IOException)
            {
                count = 0;
            }

            var child = new RegistryKeyNode(key.Name, key.FullPath, hasChildren: count > 0);
            if (count > 0)
            {
                child.Children.Add(RegistryKeyNode.Placeholder);
            }

            node.Children.Add(child);
        }

        node.ChildrenLoaded = true;
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
