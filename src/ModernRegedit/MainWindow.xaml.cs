using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModernRegedit.ViewModels;
using Windows.System;

namespace ModernRegedit;

public sealed partial class MainWindow : Window
{
    private bool _suppressSelectionEvents;

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
        if (_suppressSelectionEvents)
        {
            return;
        }

        if (args.AddedItems.Count > 0 && args.AddedItems[0] is RegistryKeyNode node)
        {
            ViewModel.Select(node);
            ViewModel.PushHistory(node.FullPath);
        }
    }

    private async void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        RegistryKeyNode? node = await ViewModel.NavigateToAsync(AddressBar.Text);
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: true);
        }
    }

    private async void OnBackClick(object sender, RoutedEventArgs e)
    {
        RegistryKeyNode? node = await ViewModel.GoBackAsync();
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: false);
        }
    }

    private async void OnForwardClick(object sender, RoutedEventArgs e)
    {
        RegistryKeyNode? node = await ViewModel.GoForwardAsync();
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: false);
        }
    }

    private async void OnUpClick(object sender, RoutedEventArgs e)
    {
        RegistryKeyNode? node = await ViewModel.GoUpAsync();
        if (node is not null && !string.Equals(node.FullPath, ViewModel.SelectedNode?.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectTreeNode(node, recordHistory: true);
        }
    }

    private void SelectTreeNode(RegistryKeyNode node, bool recordHistory)
    {
        _suppressSelectionEvents = true;
        try
        {
            KeyTree.SelectedItem = node;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        ViewModel.Select(node);
        if (recordHistory)
        {
            ViewModel.PushHistory(node.FullPath);
        }

        _ = BringIntoViewAsync(node);
    }

    /// <summary>等目标节点随级联展开物化后滚入视口；最多等约 3 秒，超时静默放弃。</summary>
    private async Task BringIntoViewAsync(RegistryKeyNode node)
    {
        for (int i = 0; i < 120; i++)
        {
            if (KeyTree.ContainerFromItem(node) is TreeViewItem container)
            {
                container.StartBringIntoView();
                return;
            }

            await Task.Delay(25);
        }
    }
}
