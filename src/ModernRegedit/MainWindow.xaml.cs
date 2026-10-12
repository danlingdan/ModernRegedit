using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernRegedit.ViewModels;

namespace ModernRegedit;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    }

    private void OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is RegistryKeyNode node)
        {
            _ = ViewModel.EnsureChildrenLoadedAsync(node);
        }
    }

    private void OnSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is RegistryKeyNode node)
        {
            ViewModel.Select(node);
        }
    }
}
