using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernRegedit.ViewModels;

public partial class RegistryKeyNode : ObservableObject
{
    public RegistryKeyNode(string title, string fullPath, bool isPlaceholder = false)
    {
        Title = title;
        FullPath = fullPath;
        IsPlaceholder = isPlaceholder;
    }

    public static RegistryKeyNode NewPlaceholder() => new("…", string.Empty, isPlaceholder: true);

    public string Title { get; }

    public string FullPath { get; }

    public bool IsPlaceholder { get; }

    public bool ChildrenLoaded { get; set; }

    public ObservableCollection<RegistryKeyNode> Children { get; } = new();
}
