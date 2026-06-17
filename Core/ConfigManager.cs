using System;
using System.IO;
using System.Text.Json;

namespace TwitchChatCore.Core;

public static class ConfigManager
{
    public static string AppDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ConfigPath = Path.Combine(AppDir, "config.json");
    
    public static AppSettings Settings { get; private set; } = new AppSettings();

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                }
            }
            else
            {
                Save(); // create default
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex}");
        }
    }

    public static void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Settings, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex}");
        }
    }
}
