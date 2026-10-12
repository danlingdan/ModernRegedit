using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernRegedit.ViewModels;

public partial class RegistryKeyNode : ObservableObject
{
    public RegistryKeyNode(string title, string fullPath)
    {
        Title = title;
        FullPath = fullPath;
        // 子键是否存在要等首次枚举才知道；先按有子键显示展开符，避免逐键计数查询。
        HasUnrealizedChildren = true;
    }

    public string Title { get; }

    public string FullPath { get; }

    public bool ChildrenLoaded { get; set; }

    /// <summary>进行中的子键枚举任务，ViewModel 用于去重并发加载。</summary>
    public Task? LoadTask { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasUnrealizedChildren { get; set; }

    public ObservableCollection<RegistryKeyNode> Children { get; } = new();
}
