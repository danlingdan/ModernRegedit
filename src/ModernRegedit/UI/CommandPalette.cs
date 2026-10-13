using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModernRegedit.UI;

/// <summary>Ctrl+K 命令面板：内置命令过滤 + 键路径直达。</summary>
public static class CommandPalette
{
    public sealed record Command(string Name, Func<Task> Run);

    public sealed record PaletteResult(Command? Chosen, string? GoPath);

    public static async Task<PaletteResult> ShowAsync(
        XamlRoot root, IReadOnlyList<Command> commands, Func<string, bool> canNavigate)
    {
        PaletteResult result = new(null, null);
        var input = new TextBox
        {
            Header = "Type a command, or a key path (contains '\\')",
            PlaceholderText = "e.g. undo  /  HKLM\\SOFTWARE",
        };

        var list = new ListBox { Height = 260, FontSize = 13 };

        void Repopulate()
        {
            list.Items.Clear();
            string filter = input.Text.Trim();

            if (canNavigate(filter))
            {
                list.Items.Add($"Go to:  {filter}");
            }

            foreach (Command command in commands.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                list.Items.Add(command.Name);
            }
        }

        input.TextChanged += (_, _) => Repopulate();
        Repopulate();

        void RunSelected()
        {
            if (list.SelectedItem is not string text)
            {
                return;
            }

            if (text.StartsWith("Go to:", StringComparison.Ordinal))
            {
                string path = text["Go to:".Length..].Trim();
                if (canNavigate(path))
                {
                    result = new PaletteResult(null, path);
                    dialog.Hide();
                }
                return;
            }

            Command? command = commands.FirstOrDefault(c => c.Name == text);
            if (command is not null)
            {
                result = new PaletteResult(command, null);
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
            Title = "Command palette (Ctrl+K)",
            Content = new StackPanel { Spacing = 8, MinWidth = 460, Children = { input, list } },
            PrimaryButtonText = "Run",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (list.SelectedItem is string)
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
