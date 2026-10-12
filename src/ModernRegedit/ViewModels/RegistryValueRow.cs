namespace ModernRegedit.ViewModels;

public sealed class RegistryValueRow
{
    public required string Name { get; init; }

    public required string KindDisplay { get; init; }

    public required string DataDisplay { get; init; }
}
