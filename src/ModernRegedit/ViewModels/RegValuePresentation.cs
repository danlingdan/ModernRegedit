using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

/// <summary>值类型到图标与颜色的映射（图标 + 颜色 + 文字三重编码）。</summary>
internal static class RegValuePresentation
{
    public static Symbol GetSymbol(RegKind kind) => kind switch
    {
        RegKind.Sz => Symbol.Character,
        RegKind.ExpandSz => Symbol.Font,
        RegKind.Binary => Symbol.Library,
        RegKind.Dword or RegKind.DwordBigEndian or RegKind.Qword => Symbol.Calculator,
        RegKind.MultiSz => Symbol.List,
        RegKind.Link => Symbol.Link,
        _ => Symbol.Document,
    };

    public static SolidColorBrush GetBrush(RegKind kind) => (SolidColorBrush)Application.Current.Resources[kind switch
    {
        RegKind.Sz => "TypeStringBrush",
        RegKind.ExpandSz => "TypeExpandStringBrush",
        RegKind.Binary => "TypeBinaryBrush",
        RegKind.Dword or RegKind.DwordBigEndian or RegKind.Qword => "TypeNumberBrush",
        RegKind.MultiSz => "TypeMultiStringBrush",
        RegKind.Link => "TypeLinkBrush",
        _ => "TypeOtherBrush",
    }];
}
