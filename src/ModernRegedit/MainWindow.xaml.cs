using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModernRegedit.Services;
using ModernRegedit.UI;
using ModernRegedit.ViewModels;
using Windows.System;

namespace ModernRegedit;

public sealed partial class MainWindow : Window
{
    private static readonly ElementTheme[] ThemeCycle =
    [
        ElementTheme.Default,
        ElementTheme.Light,
        ElementTheme.Dark,
    ];

    private static readonly string[] ThemeLabels = ["System", "Light", "Dark"];

    private bool _suppressSelectionEvents;

    private int _themeIndex;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ApplyTitleBarTheme();
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

    private async void OnViewToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not AppBarToggleButton toggle)
        {
            return;
        }

        RegistryKeyNode? node = await ViewModel.SetViewAsync(toggle.IsChecked == true);
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: false);
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _themeIndex = (_themeIndex + 1) % ThemeCycle.Length;
        RootGrid.RequestedTheme = ThemeCycle[_themeIndex];
        ThemeButton.Label = ThemeLabels[_themeIndex];
        ApplyTitleBarTheme();
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.SearchNowAsync();
        }
    }

    private void OnCancelSearch(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelSearch();
    }

    private void OnCloseSearch(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseSearchPanel();
    }

    private async void OnValuesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditValueAsync(ViewModel.SelectedValueRow);
    }

    private void OnValuesRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        RegistryValueRow? row = (e.OriginalSource as FrameworkElement)?.DataContext as RegistryValueRow;
        var menu = new MenuFlyout();

        if (row is not null && !row.IsUnset)
        {
            var modify = new MenuFlyoutItem { Text = "Modify…" };
            modify.Click += (_, _) => _ = EditValueAsync(row);
            menu.Items.Add(modify);

            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (_, _) => _ = DeleteValueAsync(row);
            menu.Items.Add(delete);

            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var newValue = new MenuFlyoutItem { Text = "New Value…" };
        newValue.Click += (_, _) => _ = NewValueAsync();
        menu.Items.Add(newValue);

        menu.ShowAt(ValuesList, e.GetPosition(ValuesList));
    }

    private async Task EditValueAsync(RegistryValueRow? row)
    {
        if (row is null || ViewModel.SelectedNode is null)
        {
            return;
        }

        string title = row.IsUnset ? "Set the (Default) value" : $"Edit value '{row.Name}'";
        byte[] initial = row.IsUnset ? Array.Empty<byte>() : row.Data;
        var edited = await ValueDialogs.ShowAsync(
            Content.XamlRoot, title, row.ValueName, row.Kind, initial, allowPickType: false);
        if (edited is null)
        {
            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.SetValue, ViewModel.SelectedNode.FullPath, edited.Value.Name, ValueType: edited.Value.Kind, Data: edited.Value.Data),
        ]);
    }

    private async Task NewValueAsync()
    {
        if (ViewModel.SelectedNode is null)
        {
            return;
        }

        var created = await ValueDialogs.ShowAsync(
            Content.XamlRoot, $"New value under '{ViewModel.SelectedNode.FullPath}'", "New Value", RegKind.Sz, Array.Empty<byte>(), allowPickType: true);
        if (created is null)
        {
            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.SetValue, ViewModel.SelectedNode.FullPath, created.Value.Name, ValueType: created.Value.Kind, Data: created.Value.Data),
        ]);
    }

    private async Task DeleteValueAsync(RegistryValueRow row)
    {
        if (ViewModel.SelectedNode is null)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            Content.XamlRoot, "Delete value", $"Delete value '{row.Name}' from '{ViewModel.SelectedNode.FullPath}'?", "Delete");
        if (!confirmed)
        {
            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.DeleteValue, ViewModel.SelectedNode.FullPath, row.ValueName),
        ]);
    }

    private static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnResultsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        JumpToResult(ResultsList.SelectedItem as SearchResultRow);
    }

    private void OnResultsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            JumpToResult(ResultsList.SelectedItem as SearchResultRow);
        }
    }

    private async void JumpToResult(SearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        // 空串表示默认值命中；null 表示仅键命中（不选值行）
        ViewModel.PendingJumpValueName = row.ValueName switch
        {
            "" => null,
            "(key)" => null,
            "(Default)" => string.Empty,
            string name => name,
        };

        RegistryKeyNode? node = await ViewModel.NavigateToAsync(row.KeyPath);
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: true);
        }
    }

    /// <summary>标题栏配色跟随内容主题，避免暗色内容配亮色标题栏的割裂感。</summary>
    private void ApplyTitleBarTheme()
    {
        Microsoft.UI.Windowing.AppWindow? appWindow = AppWindow;
        if (appWindow is null)
        {
            return;
        }

        bool dark = ThemeCycle[_themeIndex] switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => RootGrid.ActualTheme == ElementTheme.Dark,
        };

        Windows.UI.Color background = dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        Windows.UI.Color foreground = dark
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0);

        appWindow.TitleBar.BackgroundColor = background;
        appWindow.TitleBar.ForegroundColor = foreground;
        appWindow.TitleBar.ButtonBackgroundColor = background;
        appWindow.TitleBar.ButtonForegroundColor = foreground;
        appWindow.TitleBar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 45, 45, 45)
            : Windows.UI.Color.FromArgb(255, 218, 218, 218);
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
