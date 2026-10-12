using ModernRegedit.Services;

namespace ModernRegedit.ViewModels;

public sealed class RegistryValueRow
{
    public required string Name { get; init; }

    /// <summary>原始值名；空串表示默认值。</summary>
    public required string ValueName { get; init; }

    public required RegKind Kind { get; init; }

    public required byte[] Data { get; init; }

    /// <summary>默认值未设置时为 true（列表保留占位行）。</summary>
    public required bool IsUnset { get; init; }

    public required string KindDisplay { get; init; }

    public required string DataDisplay { get; init; }

    public Microsoft.UI.Xaml.Controls.Symbol KindSymbol => RegValuePresentation.GetSymbol(Kind);

    public Microsoft.UI.Xaml.Media.SolidColorBrush KindBrush => RegValuePresentation.GetBrush(Kind);
}
