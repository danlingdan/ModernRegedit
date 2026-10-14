using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernRegedit.Services;

/// <summary>应用会话设置（收藏夹、位置记忆），JSON 持久化到 LocalAppData。</summary>
public sealed class SettingsStore
{
    public List<string> Favorites { get; set; } = [];

    [JsonPropertyName("lastKey")]
    public string? LastKey { get; set; }

    [JsonPropertyName("is32BitView")]
    public bool Is32BitView { get; set; }

    /// <summary>主题：0=跟随系统 1=亮 2=暗。</summary>
    [JsonPropertyName("theme")]
    public int Theme { get; set; }

    [JsonPropertyName("backupRetention")]
    public int BackupRetention { get; set; } = 200;

    /// <summary>窗口位置记忆（物理像素）；null = 从未保存，用系统默认。</summary>
    [JsonPropertyName("windowX")]
    public int? WindowX { get; set; }

    [JsonPropertyName("windowY")]
    public int? WindowY { get; set; }

    [JsonPropertyName("windowWidth")]
    public int? WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public int? WindowHeight { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModernRegedit", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(FilePath), JsonOptions) ?? new SettingsStore();
                settings.Favorites = (settings.Favorites ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                settings.Theme = Math.Clamp(settings.Theme, 0, 2);
                settings.BackupRetention = Math.Clamp(settings.BackupRetention, 10, 1000);
                return settings;
            }
        }
        catch
        {
            // 设置损坏时按默认处理。
        }

        return new SettingsStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch
        {
            // 设置保存失败不阻塞退出。
        }
    }
}
