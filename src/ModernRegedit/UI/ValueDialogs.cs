using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernRegedit.Services;
using Microsoft.UI.Xaml.Automation;

using ModernRegedit.ViewModels;

namespace ModernRegedit.UI;

/// <summary>值编辑对话框：按类型出编辑器，产出原始注册表字节。</summary>
internal static class ValueDialogs
{
    private static readonly RegKind[] EditableKinds =
    [
        RegKind.Sz,
        RegKind.ExpandSz,
        RegKind.MultiSz,
        RegKind.Dword,
        RegKind.Qword,
        RegKind.Binary,
        RegKind.None,
    ];

    /// <summary>弹出编辑/新建值对话框；用户确认返回三元组，取消返回 null。</summary>
    public static async Task<(string Name, RegKind Kind, byte[] Data)?> ShowAsync(
        XamlRoot root, string title, string valueName, RegKind kind, byte[] data, bool allowPickType)
    {
        (string Name, RegKind Kind, byte[] Data)? result = null;

        var nameBox = new TextBox { Header = MainViewModel.Localize("VD_NameHeader"), Text = valueName };
        AutomationProperties.SetAutomationId(nameBox, "DialogNameBox");

        var typeCombo = new ComboBox { Header = MainViewModel.Localize("VD_TypeHeader"), Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (RegKind k in EditableKinds)
        {
            typeCombo.Items.Add(new ComboBoxItem { Content = RegValueDecoder.KindName(k), Tag = k });
        }

        var editor = BuildEditor(kind, data);
        var editorHost = new Grid();
        editorHost.Children.Add(editor.Panel);

        var errorText = new TextBlock
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
        };

        var rootPanel = new StackPanel { Spacing = 12, MinWidth = 380 };
        rootPanel.Children.Add(nameBox);
        rootPanel.Children.Add(editorHost);
        rootPanel.Children.Add(errorText);
        if (allowPickType)
        {
            typeCombo.SelectedIndex = IndexOfKind(kind);
            typeCombo.SelectionChanged += (_, _) =>
            {
                if (typeCombo.SelectedItem is ComboBoxItem item && item.Tag is RegKind selected)
                {
                    byte[] current = TryRead(editor.ReadBack) ?? data;
                    editor = BuildEditor(selected, current);
                    editorHost.Children.Clear();
                    editorHost.Children.Add(editor.Panel);
                }
            };
            rootPanel.Children.Insert(1, typeCombo);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = rootPanel,
            PrimaryButtonText = MainViewModel.Localize("Dialog_OK"),
            CloseButtonText = MainViewModel.Localize("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0 && valueName.Length != 0)
            {
                // 原值名非空（不是默认值）时，新名字不能为空。
                errorText.Text = MainViewModel.Localize("VD_NameRequired");
                args.Cancel = true;
                return;
            }

            byte[]? parsed = TryRead(editor.ReadBack);
            if (parsed is null)
            {
                errorText.Text = MainViewModel.Localize("VD_InvalidData");
                args.Cancel = true;
                return;
            }

            RegKind selected = allowPickType && typeCombo.SelectedItem is ComboBoxItem item && item.Tag is RegKind k
                ? k
                : kind;
            result = (name, selected, parsed);
        };

        ContentDialogResult choice = await dialog.ShowAsync();
        return choice == ContentDialogResult.Primary ? result : null;
    }

    private static int IndexOfKind(RegKind kind)
    {
        for (int i = 0; i < EditableKinds.Length; i++)
        {
            if (EditableKinds[i] == kind)
            {
                return i;
            }
        }

        return 0;
    }

    private static (FrameworkElement Panel, Func<byte[]?> ReadBack) BuildEditor(RegKind kind, byte[] data) => kind switch
    {
        RegKind.Sz or RegKind.ExpandSz => BuildStringEditor(kind, data),
        RegKind.MultiSz => BuildMultiStringEditor(data),
        RegKind.Dword or RegKind.Qword => BuildNumberEditor(kind, data),
        _ => BuildHexEditor(data),
    };

    private static (FrameworkElement Panel, Func<byte[]?> ReadBack) BuildStringEditor(RegKind kind, byte[] data)
    {
        var box = new TextBox
        {
            Header = MainViewModel.Localize("VD_StringHeader"),
            AcceptsReturn = true,
            Height = 96,
            TextWrapping = TextWrapping.Wrap,
            Text = RegValueDecoder.Decode(kind, data),
        };
        AutomationProperties.SetAutomationId(box, "DialogEditorBox");
        return (box, () => Encoding.Unicode.GetBytes(box.Text + "\0"));
    }

    private static (FrameworkElement Panel, Func<byte[]?> ReadBack) BuildMultiStringEditor(byte[] data)
    {
        string raw = Encoding.Unicode.GetString(data);
        string[] parts = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var box = new TextBox
        {
            Header = MainViewModel.Localize("VD_MultiHeader"),
            AcceptsReturn = true,
            Height = 140,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            Text = parts.Length == 0 ? string.Empty : string.Join("\r\n", parts),
        };
        AutomationProperties.SetAutomationId(box, "DialogEditorBox");
        return (box, () =>
        {
            string[] lines = box.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                sb.Append(line).Append('\0');
            }

            sb.Append('\0');
            return Encoding.Unicode.GetBytes(sb.ToString());
        });
    }

    private static (FrameworkElement Panel, Func<byte[]?> ReadBack) BuildNumberEditor(RegKind kind, byte[] data)
    {
        bool is32 = kind == RegKind.Dword;
        ulong initial = is32
            ? (data.Length >= 4 ? BitConverter.ToUInt32(data) : 0)
            : (data.Length >= 8 ? BitConverter.ToUInt64(data) : 0);

        bool hex = true;
        var box = new TextBox
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
        };
        AutomationProperties.SetAutomationId(box, "DialogEditorBox");
        var hexRadio = new RadioButton { Content = MainViewModel.Localize("VD_Hex"), IsChecked = true, GroupName = $"num{kind}" };
        var decRadio = new RadioButton { Content = MainViewModel.Localize("VD_Decimal"), GroupName = $"num{kind}" };

        void SetText(ulong v)
        {
            box.Header = MainViewModel.Localize("VD_IntHeader", is32 ? 32 : 64, hex ? MainViewModel.Localize("VD_Hex") : MainViewModel.Localize("VD_Decimal"));
            box.Text = is32
                ? (hex ? ((uint)v).ToString("X8", CultureInfo.InvariantCulture) : ((uint)v).ToString(CultureInfo.InvariantCulture))
                : (hex ? v.ToString("X16", CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture));
        }

        hexRadio.Checked += (_, _) =>
        {
            hex = true;
            if (TryReadNumber(box.Text, is32, isHex: false, out ulong v))
            {
                SetText(v);
            }
        };
        decRadio.Checked += (_, _) =>
        {
            hex = false;
            if (TryReadNumber(box.Text, is32, isHex: true, out ulong v))
            {
                SetText(v);
            }
        };
        SetText(initial);

        var row = new StackPanel { Spacing = 12, Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(hexRadio);
        row.Children.Add(decRadio);

        return (row, () =>
        {
            if (!TryReadNumber(box.Text, is32, isHex: hex, out ulong v))
            {
                return null;
            }

            return is32
                ? BitConverter.GetBytes((uint)v)
                : BitConverter.GetBytes(v);
        });
    }

    private static byte[]? TryRead(Func<byte[]?> readBack)
    {
        try
        {
            return readBack();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadNumber(string text, bool is32, bool isHex, out ulong value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            isHex = true;
        }

        try
        {
            var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            value = is32
                ? uint.Parse(text, style, CultureInfo.InvariantCulture)
                : ulong.Parse(text, style, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static (FrameworkElement Panel, Func<byte[]?> ReadBack) BuildHexEditor(byte[] data)
    {
        var box = new TextBox
        {
            Header = MainViewModel.Localize("VD_HexBytesHeader"),
            AcceptsReturn = true,
            Height = 160,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            Text = HexEncode(data),
        };
        AutomationProperties.SetAutomationId(box, "DialogEditorBox");
        return (box, () => HexDecode(box.Text));
    }

    internal static string HexEncode(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        foreach (byte b in data)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            sb.Append(' ');
        }

        if (sb.Length > 0)
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    private static byte[]? HexDecode(string text)
    {
        var bytes = new List<byte>(text.Length / 2);
        var digits = new StringBuilder(2);
        foreach (char c in text)
        {
            if (Uri.IsHexDigit(c))
            {
                digits.Append(c);
                if (digits.Length == 2)
                {
                    bytes.Add(byte.Parse(digits.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    digits.Clear();
                }
            }
            else if (!char.IsWhiteSpace(c))
            {
                return null;
            }
        }

        return digits.Length > 0 ? null : bytes.ToArray();
    }
}
