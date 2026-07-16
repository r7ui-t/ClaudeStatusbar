using System.Text.Json;

namespace QuotaBar;

/// <summary>
/// アプリ設定の永続化。%APPDATA%\QuotaBar\settings.json に保存し、
/// アイコン表示スタイルなどのユーザー選択を次回起動へ引き継ぐ。
/// </summary>
public sealed class Settings
{
    // JSON に保存する値。enum は文字列で持つ方が将来の項目追加・可読性に有利
    // 既定は最も読みやすい Number（数字）。B(リング)はメニューから選択可
    public string IconStyle { get; set; } = nameof(QuotaBar.IconStyle.Number);

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuotaBar");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private static string LegacyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeStatusbar",
        "settings.json");

    public IconStyle Style =>
        Enum.TryParse<IconStyle>(IconStyle, out var s) ? s : QuotaBar.IconStyle.Number;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Read(FilePath) ?? new Settings();

            if (File.Exists(LegacyFilePath))
            {
                var migrated = Read(LegacyFilePath);
                migrated?.Save();
                return migrated ?? new Settings();
            }
        }
        catch { /* 壊れていても既定値で続行 */ }
        return new Settings();
    }

    private static Settings? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失敗は致命的でないので握りつぶす */ }
    }
}
