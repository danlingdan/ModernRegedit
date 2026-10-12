using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernRegedit.ViewModels;

public partial class RegistryKeyNode : ObservableObject
{
    public static readonly RegistryKeyNode Placeholder = new("…", string.Empty, isPlaceholder: true);

    public RegistryKeyNode(string title, string fullPath, bool isPlaceholder = false, bool hasChildren = false)
    {
        Title = title;
        FullPath = fullPath;
        IsPlaceholder = isPlaceholder;
        HasChildren = hasChildren;
    }

    public string Title { get; }

    public string FullPath { get; }

    public bool IsPlaceholder { get; }

    public bool HasChildren { get; }

    public bool ChildrenLoaded { get; set; }

    public ObservableCollection<RegistryKeyNode> Children { get; } = new();
}
