namespace ModernRegedit.ViewModels;

public sealed class RegistryValueRow
{
    public required string Name { get; init; }

    public required string KindDisplay { get; init; }

    public required string DataDisplay { get; init; }

    public required Microsoft.UI.Xaml.Controls.Symbol KindSymbol { get; init; }

    public required Microsoft.UI.Xaml.Media.SolidColorBrush KindBrush { get; init; }
}
