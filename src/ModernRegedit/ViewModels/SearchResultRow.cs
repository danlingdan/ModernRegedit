namespace ModernRegedit.ViewModels;

public sealed class SearchResultRow
{
    public required string KeyPath { get; init; }

    /// <summary>空串表示键名命中；"(Default)" 表示默认值。</summary>
    public required string ValueName { get; init; }

    public required string DataPreview { get; init; }
}
