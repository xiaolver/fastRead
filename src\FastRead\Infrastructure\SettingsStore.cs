using System.Text.Json;
using FastRead.Core;

namespace FastRead.Infrastructure;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true
    };

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastRead");

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            // The first prototype used OpenAI defaults. Migrate an untouched prototype
            // configuration so a fresh user only needs to enter the MiniMax key.
            if (settings.ApiUrl.Equals("https://api.openai.com/v1/chat/completions", StringComparison.OrdinalIgnoreCase) &&
                settings.Model.Equals("gpt-4.1-mini", StringComparison.OrdinalIgnoreCase))
            {
                settings.ApiUrl = "https://api.minimaxi.com/v1/chat/completions";
                settings.Model = "MiniMax-M3";
            }
            return settings;
        }
        catch (JsonException)
        {
            BackupInvalidSettings();
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, SettingsPath, true);
    }

    private void BackupInvalidSettings()
    {
        try
        {
            var backup = SettingsPath + $".invalid-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(SettingsPath, backup, true);
        }
        catch
        {
            // Invalid settings should not prevent the app from starting.
        }
    }
}
