using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace TwitchChatCore.Server;

public class BadgeManager
{

    private readonly ConcurrentDictionary<string, string> _globalBadges = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _channelBadges = new(StringComparer.OrdinalIgnoreCase);
    
    public BadgeManager() 
    { 
    }

    public async Task LoadGlobalBadgesAsync()
    {
        var cacheDir = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "cache", "metadata");
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
        var cacheFile = Path.Combine(cacheDir, "global_badges.json");

        bool loadedFromCache = false;

        // Always try to load from cache first if it exists to prevent empty badges
        try
        {
            if (File.Exists(cacheFile))
            {
                var json = await File.ReadAllTextAsync(cacheFile);
                ParseAndPopulate(json, _globalBadges);
                loadedFromCache = true;
                Console.WriteLine($"Loaded {_globalBadges.Count} global badges from cache.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading global badges cache: {ex.Message}");
        }

        if (!loadedFromCache)
        {
            try
            {
                var embeddedFile = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "Server", "Resources", "global_badges.json");
                if (File.Exists(embeddedFile))
                {
                    var json = await File.ReadAllTextAsync(embeddedFile);
                    ParseAndPopulate(json, _globalBadges);
                    loadedFromCache = true;
                    Console.WriteLine($"Loaded {_globalBadges.Count} global badges from embedded resources.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading embedded badges: {ex.Message}");
            }
        }

        // Fetch updates if cache is older than 7 days or doesn't exist
        if (!loadedFromCache || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays >= 7)
        {
            try
            {
                var githubUrl = TwitchChatCore.Core.ConfigManager.Settings.GithubBadgesUrl;
                string json = "";
                bool fetchSuccess = false;
                
                try
                {
                    // Try Github first
                    json = await TwitchChatCore.Core.NetworkManager.GetClient().GetStringAsync(githubUrl);
                    fetchSuccess = true;
                    Console.WriteLine("Successfully fetched global badges from GitHub repository.");
                }
                catch
                {
                    // Fallback to IVR if Github is 404/not found
                    Console.WriteLine("GitHub repo badges not found, falling back to IVR API.");
                    json = await TwitchChatCore.Core.NetworkManager.GetClient().GetStringAsync("https://api.ivr.fi/v2/twitch/badges/global");
                    fetchSuccess = true;
                }

                if (fetchSuccess && !string.IsNullOrEmpty(json))
                {
                    await File.WriteAllTextAsync(cacheFile, json);
                    ParseAndPopulate(json, _globalBadges);
                    Console.WriteLine($"Updated {_globalBadges.Count} global badges.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading global badges from APIs: {ex.Message}");
            }
        }
    }

    public async Task LoadChannelBadgesAsync(string channelLogin)
    {
        try
        {
            var cacheDir = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "cache", "metadata");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, $"channel_{channelLogin}_badges.json");

            string json = "";
            bool loadedFromCache = false;

            if (File.Exists(cacheFile))
            {
                json = await File.ReadAllTextAsync(cacheFile);
                loadedFromCache = true;
                _channelBadges.Clear();
                ParseAndPopulate(json, _channelBadges);
            }

            // Always update channel badges in background if older than 1 day or not cached
            if (!loadedFromCache || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays > 1)
            {
                var newJson = await TwitchChatCore.Core.NetworkManager.GetClient().GetStringAsync($"https://api.ivr.fi/v2/twitch/badges/channel?login={channelLogin}");
                
                await File.WriteAllTextAsync(cacheFile, newJson);
                _channelBadges.Clear();
                ParseAndPopulate(newJson, _channelBadges);
            }
            Console.WriteLine($"Loaded {_channelBadges.Count} channel badges for {channelLogin}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading channel badges for {channelLogin}: {ex.Message}");
        }
    }

    private void ParseAndPopulate(string json, ConcurrentDictionary<string, string> target)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var set in doc.RootElement.EnumerateArray())
            {
                var setId = set.GetProperty("set_id").GetString();
                if (set.TryGetProperty("versions", out var versions))
                {
                    foreach (var version in versions.EnumerateArray())
                    {
                        var id = version.GetProperty("id").GetString();
                        var imageUrl = version.GetProperty("image_url_1x").GetString();
                        if (setId != null && id != null && imageUrl != null)
                        {
                            target[$"{setId}/{id}"] = imageUrl;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing badges JSON: {ex.Message}");
        }
    }

    public string? GetBadgeUrl(string badgeStr) // format: "subscriber/3"
    {
        if (_channelBadges.TryGetValue(badgeStr, out var channelUrl))
        {
            return channelUrl;
        }
        if (_globalBadges.TryGetValue(badgeStr, out var globalUrl))
        {
            return globalUrl;
        }

        // Hardcoded fallbacks for common global badges
        var parts = badgeStr.Split('/');
        if (parts.Length == 2)
        {
            var type = parts[0];
            var version = parts[1];
            
            if (type == "moderator" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/3";
            if (type == "vip" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc744dfa6ec/3";
            if (type == "broadcaster" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/3";
            if (type == "founder" && version == "0") return "https://static-cdn.jtvnw.net/badges/v1/511b78a9-ab37-472f-9569-457753bbe7d3/3";
            if (type == "premium" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/bbbe0db0-a598-423e-86d0-f9fb98ca1933/3";
            if (type == "staff" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/d97c37bd-a6f5-4c38-8f57-4e4bef88af34/3";
            if (type == "bot-badge" && version == "1") return "https://static-cdn.jtvnw.net/badges/v1/3ffa9565-c35b-4cad-800b-041e60659cf2/3";
        }
        return null;
    }
}
