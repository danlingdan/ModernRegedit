namespace ModernRegedit.ViewModels;

public sealed class SearchResultRow
{
    public required string KeyPath { get; init; }

    /// <summary>注册表原始值名，默认值为空串。</summary>
    public required string ValueName { get; init; }

    public bool IsKeyMatch { get; init; }

    public string ValueDisplay => IsKeyMatch ? string.Empty : ValueName.Length == 0 ? MainViewModel.Localize("Value_Default") : ValueName;

    public required string DataPreview { get; init; }
}
