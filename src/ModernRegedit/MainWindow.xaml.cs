using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModernRegedit.Services;
using ModernRegedit.UI;
using ModernRegedit.ViewModels;
using Windows.System;

namespace ModernRegedit;

/// <summary>GetWindowPlacement：窗口还原边界与最大化状态（保存位置记忆用）。</summary>
internal static class WindowPlacementNative
{
    public const int SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpplwnd);
}

public sealed partial class MainWindow : Window
{
    private static readonly ElementTheme[] ThemeCycle =
    [
        ElementTheme.Default,
        ElementTheme.Light,
        ElementTheme.Dark,
    ];

    private static string ThemeLabel(int index) => index switch
    {
        1 => MainViewModel.Localize("Theme_Light"),
        2 => MainViewModel.Localize("Theme_Dark"),
        _ => MainViewModel.Localize("Theme_System"),
    };

    private bool _suppressSelectionEvents;

    private int _themeIndex;

    public MainViewModel ViewModel { get; } = new();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TreeColumn.Width = new GridLength(e.NewSize.Width < 900 ? 220 : 300);
        SearchBox.Width = e.NewSize.Width < 900 ? 160 : 220;
        ValueFilter.Width = e.NewSize.Width < 900 ? 160 : 220;
    }

    private void OnValueFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.ValueFilterText = string.Empty;
            e.Handled = true;
        }
    }

    private void OnSortValuesClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string column }) ViewModel.SortValues(int.Parse(column));
    }

    private void OnNewKeyClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(() => NewKeyAsync(ViewModel.SelectedNode));
    private void OnNewValueClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(NewValueAsync);
    private void OnImportClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(ImportAsync);
    private void OnExportClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(() => ExportAsync(ViewModel.SelectedNode));
    private void OnRefreshClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(ViewModel.RefreshCurrentAsync);
    private void OnCommandsClick(object sender, RoutedEventArgs e) =>
        _ = RunOnUiThreadAsync(ShowCommandPaletteAsync);

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // 集成式标题栏：内容贯通到标题栏，Mica 显示；SetTitleBar 用无交互元素做整行拖拽区。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        Activated += OnWindowActivated;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyStartupSettings();
        ApplyWindowPlacement();
        AppTitleText.Text = "ModernRegedit · 1.0.1 rc.1";
        Closed += (_, _) =>
        {
            SaveWindowPlacement();
            ViewModel.PersistSession();
        };
        _ = RestoreLastLocationAsync();
    }

    /// <summary>启动时恢复上次窗口位置，钳制到最近工作区内。</summary>
    private void ApplyWindowPlacement()
    {
        if (ViewModel.WindowPlacement is not { } p)
        {
            return;
        }

        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        int w = Math.Clamp(p.W, Math.Min(680, area.WorkArea.Width), area.WorkArea.Width);
        int h = Math.Clamp(p.H, 350, area.WorkArea.Height);
        int x = Math.Clamp(p.X, area.WorkArea.X - w + 120, area.WorkArea.X + area.WorkArea.Width - 120);
        int y = Math.Clamp(p.Y, area.WorkArea.Y, area.WorkArea.Y + area.WorkArea.Height - 120);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));

        if (p.Maximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    /// <summary>关闭前保存窗口位置；最大化时用 GetWindowPlacement 的还原边界。</summary>
    private void SaveWindowPlacement()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var placement = new WindowPlacementNative.WINDOWPLACEMENT { length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WindowPlacementNative.WINDOWPLACEMENT>() };
        int x, y, w, h;
        bool maximized;
        if (WindowPlacementNative.GetWindowPlacement(hwnd, ref placement))
        {
            maximized = placement.showCmd == WindowPlacementNative.SW_SHOWMAXIMIZED;
            x = placement.rcNormalPosition.Left;
            y = placement.rcNormalPosition.Top;
            w = placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;
            h = placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;
        }
        else
        {
            maximized = false;
            x = AppWindow.Position.X;
            y = AppWindow.Position.Y;
            w = AppWindow.Size.Width;
            h = AppWindow.Size.Height;
        }

        ViewModel.UpdateWindowPlacement(x, y, w, h, maximized);
    }

    private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        AppTitleText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.6 : 1;
        UpdateCaptionColors();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateCaptionColors();

    private void UpdateCaptionColors()
    {
        if (RootGrid is null) return;
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var caption = AppWindow.TitleBar;
        caption.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        caption.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        caption.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        caption.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedNode))
        {
            UpdateWindowTitle();
        }
    }

    /// <summary>任务栏/Alt+Tab 标题带上当前键路径，便于辨认窗口位置。</summary>
    private void UpdateWindowTitle()
    {
        string? path = ViewModel.SelectedNode?.FullPath;
        Title = string.IsNullOrEmpty(path) ? "ModernRegedit" : $"{path} - ModernRegedit";
    }

    /// <summary>启动时应用持久化设置：初始主题与备份保留份数。</summary>
    private void ApplyStartupSettings()
    {
        if (ViewModel.ThemeSetting is >= 0 and <= 2)
        {
            _themeIndex = ViewModel.ThemeSetting;
            RootGrid.RequestedTheme = ThemeCycle[_themeIndex];
            ThemeButton.Label = ThemeLabel(_themeIndex);
        }

        // 恢复 32 位视图时同步开关；事件触发 SetViewAsync 会因视图相同而空转，无副作用。
        View32Toggle.IsChecked = ViewModel.Is32BitView;

        if (ViewModel.BackupRetentionSetting > 0)
        {
            Services.RegistryWriteService.BackupRetentionOverride = ViewModel.BackupRetentionSetting;
        }
    }

    /// <summary>位置记忆：启动时回到上次退出所在键。</summary>
    private async Task RestoreLastLocationAsync()
    {
        string? last = ViewModel.LastKey;
        if (string.IsNullOrEmpty(last))
        {
            return;
        }

        await Task.Delay(600); // 等首帧布局与根节点就绪
        RegistryKeyNode? node = await ViewModel.NavigateToAsync(last);
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: true);
        }
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
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            await RunOnUiThreadAsync(async () =>
            {
                RegistryKeyNode? node = await ViewModel.NavigateToAsync(AddressBar.Text);
                if (node is not null)
                {
                    SelectTreeNode(node, recordHistory: true);
                }
            });
        }
        else if (e.Key is VirtualKey.Escape)
        {
            // 还原为当前选中键路径。
            string current = ViewModel.SelectedNode?.FullPath ?? string.Empty;
            if (AddressBar.Text != current)
            {
                e.Handled = true;
                AddressBar.Text = current;
            }
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
        toggle.IsChecked = ViewModel.Is32BitView;
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: false);
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _themeIndex = (_themeIndex + 1) % ThemeCycle.Length;
        RootGrid.RequestedTheme = ThemeCycle[_themeIndex];
        ThemeButton.Label = ThemeLabel(_themeIndex);
        ViewModel.UpdateSettings(_themeIndex, ViewModel.BackupRetentionSetting);
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var retention = new NumberBox
        {
            Header = MainViewModel.Localize("Settings_BackupRetention"),
            Value = ViewModel.BackupRetentionSetting,
            Minimum = 10,
            Maximum = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };

        var themeList = new ComboBox { Header = MainViewModel.Localize("Settings_Theme"), Width = 200 };
        for (int i = 0; i < 3; i++)
        {
            themeList.Items.Add(new ComboBoxItem { Content = ThemeLabel(i) });
        }
        themeList.SelectedIndex = ViewModel.ThemeSetting;

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(themeList);
        panel.Children.Add(retention);
        panel.Children.Add(BuildAboutSection());

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = MainViewModel.Localize("Settings_Title"),
            Content = panel,
            PrimaryButtonText = MainViewModel.Localize("Dialog_Save"),
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _themeIndex = themeList.SelectedIndex;
            RootGrid.RequestedTheme = ThemeCycle[_themeIndex];
            ThemeButton.Label = ThemeLabel(_themeIndex);
            ViewModel.UpdateSettings(_themeIndex, (int)retention.Value);
            Services.RegistryWriteService.BackupRetentionOverride = (int)retention.Value;
        }
    }

    /// <summary>设置对话框的“关于”区：包版本、许可证、仓库链接、备份文件夹入口。</summary>
    private static StackPanel BuildAboutSection()
    {
        var about = new StackPanel { Spacing = 4 };
        about.Children.Add(new TextBlock
        {
            Text = MainViewModel.Localize("About_Prerelease"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        about.Children.Add(new TextBlock
        {
            Text = MainViewModel.Localize("About_Description"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        about.Children.Add(new HyperlinkButton
        {
            Content = MainViewModel.Localize("About_RepoLink"),
            NavigateUri = new Uri("https://github.com/danlingdan/ModernRegedit"),
        });

        var backups = new HyperlinkButton
        {
            Content = MainViewModel.Localize("About_BackupsLink"),
        };
        backups.Click += async (_, _) =>
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModernRegedit", "backups");
            Directory.CreateDirectory(dir);
            await Windows.System.Launcher.LaunchFolderPathAsync(dir);
        };
        about.Children.Add(backups);

        return new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock { Text = MainViewModel.Localize("About_Header"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                about,
            },
        };
    }

    private void OnFavoritesClick(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        if (ViewModel.SelectedNode is not null && ViewModel.SelectedNode.FullPath.Contains('\\'))
        {
            bool isFav = ViewModel.IsFavorite(ViewModel.SelectedNode.FullPath);
            var toggle = new MenuFlyoutItem
            {
                Text = isFav ? MainViewModel.Localize("Favorites_RemoveCurrent") : MainViewModel.Localize("Favorites_AddCurrent"),
            };
            toggle.Click += (_, _) => ViewModel.ToggleFavorite(ViewModel.SelectedNode.FullPath);
            menu.Items.Add(toggle);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (string favorite in ViewModel.Favorites)
        {
            var item = new MenuFlyoutItem { Text = favorite };
            string path = favorite;
            item.Click += async (_, _) =>
            {
                await RunOnUiThreadAsync(async () =>
                {
                    RegistryKeyNode? node = await ViewModel.NavigateToAsync(path);
                    if (node is not null)
                    {
                        SelectTreeNode(node, recordHistory: true);
                    }
                });
            };
            menu.Items.Add(item);
        }

        if (ViewModel.Favorites.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = MainViewModel.Localize("Favorites_Empty"), IsEnabled = false });
        }

        menu.ShowAt(FavoritesButton, new Windows.Foundation.Point(0, FavoritesButton.ActualHeight));
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            await RunOnUiThreadAsync(async () => await ViewModel.SearchNowAsync());
        }
        else if (e.Key is VirtualKey.Escape)
        {
            // 有词清词（联动关闭面板），无词仅收起面板。
            if (ViewModel.SearchText.Length > 0)
            {
                e.Handled = true;
                ViewModel.SearchText = string.Empty;
            }
            else if (ViewModel.SearchPanelVisibility == Visibility.Visible)
            {
                e.Handled = true;
                ViewModel.CloseSearchPanel();
            }
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
            var modify = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Modify") };
            modify.Click += async (_, _) => await RunOnUiThreadAsync(async () => await EditValueAsync(row));
            menu.Items.Add(modify);

            var delete = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Delete") };
            delete.Click += async (_, _) => await RunOnUiThreadAsync(async () => await DeleteValueAsync(row));
            menu.Items.Add(delete);

            menu.Items.Add(new MenuFlyoutSeparator());

            var copyName = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_CopyName") };
            copyName.Click += (_, _) => CopyText(row.Name);
            menu.Items.Add(copyName);

            var copyData = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_CopyData") };
            copyData.Click += (_, _) => CopyText(row.ClipboardData);
            menu.Items.Add(copyData);

            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var newValue = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_NewValue") };
        newValue.Click += async (_, _) => await RunOnUiThreadAsync(async () => await NewValueAsync());
        menu.Items.Add(newValue);

        menu.ShowAt(ValuesList, e.GetPosition(ValuesList));
    }

    /// <summary>文本进剪贴板（复制语义）。</summary>
    private static void CopyText(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage
        {
            RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy,
        };
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void OnCopyValueDataAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        RegistryValueRow? row = ViewModel.SelectedValueRow;
        if (row is null || row.IsUnset)
        {
            return;
        }

        args.Handled = true;
        CopyText(row.ClipboardData);
        ViewModel.SetStatusMessage(MainViewModel.Localize("Status_CopiedData", row.Name));
    }

    private void OnCopyKeyPathAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedNode is not RegistryKeyNode node)
        {
            return;
        }

        args.Handled = true;
        CopyText(node.FullPath);
        ViewModel.SetStatusMessage(MainViewModel.Localize("Status_CopiedPath", node.FullPath));
    }

    private async Task EditValueAsync(RegistryValueRow? row)
    {
        if (row is null || ViewModel.SelectedNode is null)
        {
            return;
        }

        string title = row.IsUnset
            ? MainViewModel.Localize("EditDefault_Title")
            : MainViewModel.Localize("EditValue_Title", row.Name);
        byte[] initial = row.IsUnset ? Array.Empty<byte>() : row.Data;
        var edited = await ValueDialogs.ShowAsync(
            Content.XamlRoot, title, row.ValueName, row.Kind, initial, allowPickType: false);
        if (edited is null)
        {
            return;
        }

        string keyPath = ViewModel.SelectedNode.FullPath;
        if (!row.IsUnset && row.ValueName.Length > 0 && !string.Equals(edited.Value.Name, row.ValueName, StringComparison.Ordinal))
        {
            // 改名 = 置新值 + 删旧值；同名编辑走普通覆盖。
            bool ok = await ViewModel.ApplyWritesAsync(
            [
                new WriteOperation(WriteOpKind.SetValue, keyPath, edited.Value.Name, ValueType: edited.Value.Kind, Data: edited.Value.Data),
                new WriteOperation(WriteOpKind.DeleteValue, keyPath, row.ValueName),
            ]);
            if (ok)
            {
                ViewModel.SetStatusMessage(MainViewModel.Localize("Status_Renamed", row.Name, edited.Value.Name));
            }

            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.SetValue, keyPath, edited.Value.Name, ValueType: edited.Value.Kind, Data: edited.Value.Data),
        ]);
    }

    private async Task NewValueAsync()
    {
        if (ViewModel.SelectedNode is null)
        {
            return;
        }

        var created = await ValueDialogs.ShowAsync(
            Content.XamlRoot, MainViewModel.Localize("NewValue_Title", ViewModel.SelectedNode.FullPath), "New Value", RegKind.Sz, Array.Empty<byte>(), allowPickType: true);
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

        bool confirmed = await ConfirmHighRiskAsync(
            Content.XamlRoot, MainViewModel.Localize("DeleteValue_Title"), ViewModel.SelectedNode.FullPath,
            MainViewModel.Localize("DeleteValue_Message", row.Name, ViewModel.SelectedNode.FullPath),
            MainViewModel.Localize("Dialog_Delete"));
        if (!confirmed)
        {
            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.DeleteValue, ViewModel.SelectedNode.FullPath, row.ValueName),
        ]);
    }

    /// <summary>
    /// 把 async 事件处理器的后续段强制归队 UI 线程。SynchronizationContext 偶发失效时
    /// （同栈 RPC_E_WRONG_THREAD 崩溃已三现），await 续体会落在线程池上，这里兜底。
    /// </summary>
    private async Task RunOnUiThreadAsync(Func<Task> work)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            try { await work(); }
            catch (Exception ex) { ViewModel.SetError(ex.Message); }
            return;
        }

        App.LogContinuationOffThread();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                App.LogCrash("RunOnUiThreadAsync: " + ex);
                ViewModel.SetError(ex.Message);
            }
            finally
            {
                completion.TrySetResult();
            }
        });
        await completion.Task;
    }

    private void OnPaletteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = RunOnUiThreadAsync(async () => await ShowCommandPaletteAsync());
    }

    /// <summary>F6 分区焦点循环：键树 → 值列表 → 地址栏。</summary>
    private void OnF6Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        object? current = FocusManager.GetFocusedElement();
        Control target = ReferenceEquals(current, KeyTree) ? ValuesList
            : ReferenceEquals(current, ValuesList) ? AddressBar
            : KeyTree;
        args.Handled = true;
        target.Focus(FocusState.Programmatic);
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = RunOnUiThreadAsync(async () => await ViewModel.RefreshCurrentAsync());
    }

    private void OnFindAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    private void OnAddressAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AddressBar.Focus(FocusState.Programmatic);
        AddressBar.SelectAll();
    }

    /// <summary>键树键盘操作与 regedit 对齐：Delete 删键，F2 改名。</summary>
    private async void OnTreeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        RegistryKeyNode? node = ViewModel.SelectedNode;
        if (node is null || !node.FullPath.Contains('\\'))
        {
            return; // 根键不可删改
        }

        switch (e.Key)
        {
            case VirtualKey.Delete:
                e.Handled = true;
                await RunOnUiThreadAsync(async () => await DeleteKeyAsync(node));
                break;
            case VirtualKey.F2:
                e.Handled = true;
                await RunOnUiThreadAsync(async () => await RenameKeyAsync(node));
                break;
        }
    }

    /// <summary>值列表键盘操作：Enter/F2 编辑，Delete 删除。</summary>
    private async void OnValuesKeyDown(object sender, KeyRoutedEventArgs e)
    {
        RegistryValueRow? row = ViewModel.SelectedValueRow;
        if (row is null)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
            case VirtualKey.F2:
                e.Handled = true;
                await RunOnUiThreadAsync(async () => await EditValueAsync(row));
                break;
            case VirtualKey.Delete:
                if (!row.IsUnset)
                {
                    e.Handled = true;
                    await RunOnUiThreadAsync(async () => await DeleteValueAsync(row));
                }
                break;
        }
    }

    private async Task ShowCommandPaletteAsync()
    {
        var commands = new List<UI.CommandPalette.Command>
        {
            new(MainViewModel.Localize("Cmd_Undo"), async () => await ViewModel.UndoAsync()),
            new(MainViewModel.Localize("Cmd_Redo"), async () => await ViewModel.RedoAsync()),
            new(MainViewModel.Localize("Cmd_ExportCurrent"), async () => await ExportAsync(ViewModel.SelectedNode)),
            new(MainViewModel.Localize("Cmd_Import"), async () => await ImportAsync()),
            new(MainViewModel.Localize("Cmd_Toggle32Bit"), () => { View32Toggle.IsChecked = !ViewModel.Is32BitView; return Task.CompletedTask; }),
            new(MainViewModel.Localize("Cmd_CycleTheme"), () => { OnThemeClick(this, new RoutedEventArgs()); return Task.CompletedTask; }),
        };

        UI.CommandPalette.PaletteResult result = await UI.CommandPalette.ShowAsync(
            Content.XamlRoot,
            commands,
            path => !string.IsNullOrWhiteSpace(path) && path.Contains('\\'));

        if (result.GoPath is not null)
        {
            await SelectPathAsync(result.GoPath);
        }
        else if (result.Chosen is not null)
        {
            await result.Chosen.Run();
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        _ = RunOnUiThreadAsync(async () => await ViewModel.UndoAsync());
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        _ = RunOnUiThreadAsync(async () => await ViewModel.RedoAsync());
    }

    private void OnUndoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement() is TextBox)
        {
            return; // 文本框内留给文本编辑
        }

        args.Handled = true;
        _ = RunOnUiThreadAsync(async () => await ViewModel.UndoAsync());
    }

    private void OnRedoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement() is TextBox)
        {
            return;
        }

        args.Handled = true;
        _ = RunOnUiThreadAsync(async () => await ViewModel.RedoAsync());
    }

    private static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = title,
            Content = message,
            PrimaryButtonText = action,
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>高危键确认：命中清单时显示分级警告（红=启动关键，黄=服务/自启动）。</summary>
    private static async Task<bool> ConfirmHighRiskAsync(
        XamlRoot root, string title, string keyPath, string message, string action)
    {
        HighRiskArea? area = HighRiskKeys.Match(keyPath);
        if (area is null)
        {
            return await ConfirmAsync(root, title, message, action);
        }

        var levelText = new TextBlock
        {
            Text = area.Level == RiskLevel.Critical
                ? MainViewModel.Localize("Risk_Critical")
                : MainViewModel.Localize("Risk_Elevated"),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                area.Level == RiskLevel.Critical ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.Goldenrod),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var reason = new TextBlock
        {
            Text = MainViewModel.Localize("Risk_Reason_" + area.Key),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(levelText);
        panel.Children.Add(body);
        panel.Children.Add(reason);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = title,
            Content = panel,
            PrimaryButtonText = action,
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task<string?> PromptTextAsync(XamlRoot root, string title, string header, string initial, bool rejectBackslash = true)
    {
        string? result = null;
        var box = new TextBox { Header = header, Text = initial };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "DialogNameBox");
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = title,
            Content = box,
            PrimaryButtonText = MainViewModel.Localize("Dialog_OK"),
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            string name = box.Text.Trim();
            if (name.Length == 0 || (rejectBackslash && name.Contains('\\')))
            {
                args.Cancel = true;
                return;
            }

            result = name;
        };

        await dialog.ShowAsync();
        return result;
    }

    private RegistryKeyNode? FindNodeUnder(object source)
    {
        DependencyObject? dep = source as DependencyObject;
        while (dep is not null and not TreeViewItem)
        {
            dep = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(dep);
        }

        return dep is TreeViewItem container ? KeyTree.ItemFromContainer(container) as RegistryKeyNode : null;
    }

    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        RegistryKeyNode? node = FindNodeUnder(e.OriginalSource);

        var menu = new MenuFlyout();

        // 根键与树空白区不可新建（与 regedit 一致）。
        if (node is not null && node.FullPath.Contains('\\'))
        {
            var newKey = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_NewKey") };
            newKey.Click += async (_, _) => await RunOnUiThreadAsync(async () => await NewKeyAsync(node));
            menu.Items.Add(newKey);

            var newValue = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_NewValue") };
            newValue.Click += async (_, _) => await RunOnUiThreadAsync(async () => await NewValueUnderAsync(node));
            menu.Items.Add(newValue);

            var rename = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Rename") };
            rename.Click += async (_, _) => await RunOnUiThreadAsync(async () => await RenameKeyAsync(node));
            menu.Items.Add(rename);

            var delete = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Delete") };
            delete.Click += async (_, _) => await RunOnUiThreadAsync(async () => await DeleteKeyAsync(node));
            menu.Items.Add(delete);

            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (node is not null)
        {
            var copyPath = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_CopyPath") };
            copyPath.Click += (_, _) => CopyText(node.FullPath);
            menu.Items.Add(copyPath);

            var export = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Export") };
            export.Click += async (_, _) => await RunOnUiThreadAsync(async () => await ExportAsync(node));
            menu.Items.Add(export);
        }

        var import = new MenuFlyoutItem { Text = MainViewModel.Localize("Menu_Import") };
        import.Click += async (_, _) => await RunOnUiThreadAsync(async () => await ImportAsync());
        menu.Items.Add(import);

        menu.ShowAt(KeyTree, e.GetPosition(KeyTree));
    }

    private async Task NewKeyAsync(RegistryKeyNode? parent)
    {
        string? name = await PromptTextAsync(
            Content.XamlRoot,
            parent is null ? MainViewModel.Localize("NewKey_Title_Root") : MainViewModel.Localize("NewKey_Title", parent.FullPath),
            MainViewModel.Localize("Prompt_KeyName"),
            "New Key");
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        string path = parent is null ? name : $"{parent.FullPath}\\{name}";
        if (await ViewModel.ApplyWritesAsync([new WriteOperation(WriteOpKind.CreateKey, path)]))
        {
            await SelectPathAsync(path);
        }
    }

    private async Task NewValueUnderAsync(RegistryKeyNode? node)
    {
        if (node is null)
        {
            return;
        }

        var created = await ValueDialogs.ShowAsync(
            Content.XamlRoot, MainViewModel.Localize("NewValue_Title", node.FullPath), "New Value", RegKind.Sz, Array.Empty<byte>(), allowPickType: true);
        if (created is null)
        {
            return;
        }

        await ViewModel.ApplyWritesAsync(
        [
            new WriteOperation(WriteOpKind.SetValue, node.FullPath, created.Value.Name, ValueType: created.Value.Kind, Data: created.Value.Data),
        ]);
    }

    private async Task RenameKeyAsync(RegistryKeyNode node)
    {
        string? newName = await PromptTextAsync(
            Content.XamlRoot, MainViewModel.Localize("Rename_Title", node.Title), MainViewModel.Localize("Prompt_NewName"), node.Title);
        if (string.IsNullOrEmpty(newName) || string.Equals(newName, node.Title, StringComparison.Ordinal))
        {
            return;
        }

        string newPath = $"{node.FullPath[..node.FullPath.LastIndexOf('\\')]}\\{newName}";
        if (await ViewModel.ApplyWritesAsync([new WriteOperation(WriteOpKind.RenameKey, node.FullPath, NewName: newName)]))
        {
            await SelectPathAsync(newPath);
        }
    }

    private async Task DeleteKeyAsync(RegistryKeyNode node)
    {
        bool confirmed = await ConfirmHighRiskAsync(
            Content.XamlRoot,
            MainViewModel.Localize("DeleteKey_Title"),
            node.FullPath,
            MainViewModel.Localize("DeleteKey_Message", node.FullPath),
            MainViewModel.Localize("Dialog_Delete"));
        if (!confirmed)
        {
            return;
        }

        string parentPath = node.FullPath[..node.FullPath.LastIndexOf('\\')];
        if (await ViewModel.ApplyWritesAsync([new WriteOperation(WriteOpKind.DeleteKey, node.FullPath)]))
        {
            await SelectPathAsync(parentPath);
        }
    }

    private async Task SelectPathAsync(string path)
    {
        RegistryKeyNode? node = await ViewModel.NavigateToAsync(path);
        if (node is not null)
        {
            SelectTreeNode(node, recordHistory: false);
        }
    }

    private async Task ExportAsync(RegistryKeyNode? node)
    {
        if (node is null)
        {
            ViewModel.SetStatusMessage(MainViewModel.Localize("Status_Export_NoNode"));
            return;
        }

        // 已知问题：本机环境下 WinUI FileSavePicker 与 Win32 IFileDialog::Show 均静默挂起（无窗口、不返回、
        // 不抛异常，疑似 #8026 同族且机器相关）。M3 改为直接导出到 Downloads，自动命名避免覆盖。
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{node.Title}.reg");
        for (int i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(dir, $"{node.Title} ({i}).reg");
        }

        await RegFile.ExportAsync(new RegistryService(), node.FullPath, ViewModel.CurrentView, path);
        ViewModel.SetStatusMessage(MainViewModel.Localize("Status_Exported", node.FullPath, path));
    }

    private async Task ImportAsync()
    {
        string? path = await PromptTextAsync(
            Content.XamlRoot, MainViewModel.Localize("Import_Title"), MainViewModel.Localize("Import_PathHeader"), string.Empty, rejectBackslash: false);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await ImportFromPathAsync(path);
    }

    /// <summary>拖放 .reg 到窗口 = 直接走导入差异预览（文件选择器在本机挂起，见已知坑）。</summary>
    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            ViewModel.SetStatusMessage(MainViewModel.Localize("Drop_Hint"));
        }
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            string? path = items
                .OfType<Windows.Storage.StorageFile>()
                .FirstOrDefault(f => f.Name.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))?.Path;
            if (path is null)
            {
                ViewModel.SetStatusMessage(MainViewModel.Localize("Drop_Hint"));
                return;
            }

            await RunOnUiThreadAsync(async () => await ImportFromPathAsync(path));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task ImportFromPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            ViewModel.SetError(MainViewModel.Localize("Import_FileNotFound", path));
            return;
        }

        var service = new RegistryService();
        RegFile.ImportPlan plan;
        RegistryView view = ViewModel.CurrentView;
        try
        {
            plan = await Task.Run(() => RegFile.BuildImportPlan(service, RegFile.Parse(path), view));
        }
        catch (Exception ex)
        {
            ViewModel.SetError(MainViewModel.Localize("Import_ParseFailed", path, ex.Message));
            return;
        }

        if (view != ViewModel.CurrentView) return;
        if (plan.Operations.Count == 0)
        {
            ViewModel.SetStatusMessage(MainViewModel.Localize("Import_NoChanges", path));
            return;
        }

        bool confirmed = await ConfirmImportAsync(plan, path);
        if (!confirmed)
        {
            return;
        }

        if (await ViewModel.ApplyWritesAsync(plan.Operations, refreshPerPath: false))
        {
            ViewModel.SetStatusMessage(MainViewModel.Localize("Import_Applied", plan.Operations.Count, path, plan.Added, plan.Modified, plan.Deleted));
        }
    }

    private async Task<bool> ConfirmImportAsync(RegFile.ImportPlan plan, string fileName)
    {
        int riskHits = plan.Operations.Count(op => HighRiskKeys.Match(op.KeyPath) is not null);
        string riskNote = riskHits > 0
            ? MainViewModel.Localize("Import_RiskNote", riskHits)
            : string.Empty;
        var summary = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Text = MainViewModel.Localize("Import_Summary", fileName, plan.Rows.Count, plan.Added, plan.Modified, plan.Deleted) + riskNote,
            TextWrapping = TextWrapping.Wrap,
        };

        var list = new ListBox
        {
            Height = 320,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            FontSize = 12,
        };
        foreach (RegFile.DiffRow row in plan.Rows.Take(300))
        {
            string valuePart = row.Value.Length == 0 ? string.Empty : $"  [{row.Value}]";
            list.Items.Add($"({row.Change}) {row.Path}{valuePart}  {row.Detail}");
        }

        if (plan.Rows.Count > 300)
        {
            list.Items.Add(MainViewModel.Localize("Import_MoreRows", plan.Rows.Count - 300));
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(summary);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = MainViewModel.Localize("Import_PreviewTitle"),
            Content = panel,
            PrimaryButtonText = MainViewModel.Localize("Dialog_Apply"),
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
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
        else if (e.Key is VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CloseSearchPanel();
        }
    }

    private async void JumpToResult(SearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        ViewModel.ValueFilterText = string.Empty;
        ViewModel.PendingJumpValueName = row.IsKeyMatch ? null : row.ValueName;

        await RunOnUiThreadAsync(async () =>
        {
            RegistryKeyNode? node = await ViewModel.NavigateToAsync(row.KeyPath);
            if (node is not null)
            {
                SelectTreeNode(node, recordHistory: true);
            }
        });
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
