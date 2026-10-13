using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using ModernRegedit.ViewModels;

namespace ModernRegedit.UI;

/// <summary>Ctrl+K 命令面板：内置命令过滤 + 键路径直达。</summary>
public static class CommandPalette
{
    public sealed record Command(string Name, Func<Task> Run);

    public sealed record PaletteResult(Command? Chosen, string? GoPath);

    /// <summary>候选项：命令或跳转路径。按索引选中，避免依赖展示文案解析。</summary>
    private sealed record Entry(string Display, Command? Cmd, string? Path);

    public static async Task<PaletteResult> ShowAsync(
        XamlRoot root, IReadOnlyList<Command> commands, Func<string, bool> canNavigate)
    {
        PaletteResult result = new(null, null);
        var input = new TextBox
        {
            Header = MainViewModel.Localize("Palette_InputHeader"),
            PlaceholderText = MainViewModel.Localize("Palette_Placeholder"),
        };

        var list = new ListBox { Height = 260, FontSize = 13 };
        List<Entry> entries = [];

        void Repopulate()
        {
            list.Items.Clear();
            entries.Clear();
            string filter = input.Text.Trim();

            if (canNavigate(filter))
            {
                entries.Add(new Entry(MainViewModel.Localize("Palette_GoTo", filter), null, filter));
            }

            foreach (Command command in commands.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new Entry(command.Name, command, null));
            }

            foreach (Entry entry in entries)
            {
                list.Items.Add(entry.Display);
            }
        }

        input.TextChanged += (_, _) => Repopulate();
        Repopulate();

        void RunSelected()
        {
            int index = list.SelectedIndex;
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            Entry entry = entries[index];
            if (entry.Path is not null)
            {
                result = new PaletteResult(null, entry.Path);
                dialog.Hide();
            }
            else if (entry.Cmd is not null)
            {
                result = new PaletteResult(entry.Cmd, null);
                dialog.Hide();
            }
        }

        list.DoubleTapped += (_, _) => RunSelected();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                RunSelected();
            }
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down && list.Items.Count > 0)
            {
                list.Focus(FocusState.Keyboard);
                list.SelectedIndex = 0;
            }
        };

        dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = MainViewModel.Localize("Palette_Title"),
            Content = new StackPanel { Spacing = 8, MinWidth = 460, Children = { input, list } },
            PrimaryButtonText = MainViewModel.Localize("Dialog_Run"),
            CloseButtonText = MainViewModel.Localize("Dialog_Close"),
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (list.SelectedIndex >= 0)
            {
                RunSelected();
            }
            else if (list.Items.Count > 0 && input.Text.Trim().Length > 0)
            {
                list.SelectedIndex = 0;
                RunSelected();
            }

            if (result.Chosen is null && result.GoPath is null)
            {
                args.Cancel = true;
            }
        };

        await dialog.ShowAsync();
        return result;
    }

    private static ContentDialog? dialog;
}
