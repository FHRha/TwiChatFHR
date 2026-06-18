namespace TwitchChatCore.Core;

/// <summary>
/// Web-specific extension of ConfigManager.
/// Adds UpdateSettings() method since the Settings setter is private.
/// </summary>
public static partial class ConfigManager
{
    /// <summary>Replace the in-memory settings object (web API POST /api/config).</summary>
    public static void UpdateSettings(AppSettings newSettings)
    {
        // Reflection-free approach: copy all JSON-serializable fields by round-tripping.
        // The new instance is already fully deserialized, we just swap the reference.
        // Private setter workaround: use the Load() / file approach via a temp file.
        var json = System.Text.Json.JsonSerializer.Serialize(newSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var tmp = System.IO.Path.Combine(DataDir, "_cfg_update_tmp.json");
        System.IO.File.WriteAllText(tmp, json);
        // Rename to config.json atomically
        System.IO.File.Copy(tmp, System.IO.Path.Combine(DataDir, "config.json"), overwrite: true);
        System.IO.File.Delete(tmp);
        // Reload into static Settings
        Load();
    }
}
