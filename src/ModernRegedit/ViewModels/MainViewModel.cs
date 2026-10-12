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
            _selectedValueCount = 0;
        }
        catch (IOException)
        {
            _selectedSubKeyCount = 0;
            _selectedValueCount = 0;
        }

        OnPropertyChanged(nameof(StatusText));
    }
}
