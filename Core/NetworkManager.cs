using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwitchChatCore.Core;

public static class NetworkManager
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private static List<string> _apiMirrors = new() { "https://7tv.io", "https://api.7tv.app" };
    private static List<string> _cdnMirrors = new() { "https://cdn.7tv.app", "https://cdn.zerotv.app" };
    private static bool _mirrorsLoaded = false;

    static NetworkManager()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TwitchChatCore/2.0");
    }

    public static async Task LoadMirrorsAsync()
    {
        if (_mirrorsLoaded) return;
        try
        {
            var json = await _httpClient.GetStringAsync("https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/mirrors.json");
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("api", out var apiElement))
            {
                var apis = new List<string>();
                foreach (var el in apiElement.EnumerateArray()) apis.Add(el.GetString()?.TrimEnd('/') ?? "");
                if (apis.Count > 0) _apiMirrors = apis;
            }

            if (doc.RootElement.TryGetProperty("cdn", out var cdnElement))
            {
                var cdns = new List<string>();
                foreach (var el in cdnElement.EnumerateArray()) cdns.Add(el.GetString()?.TrimEnd('/') ?? "");
                if (cdns.Count > 0) _cdnMirrors = cdns;
            }
            Console.WriteLine($"Loaded {_apiMirrors.Count} API mirrors and {_cdnMirrors.Count} CDN mirrors from GitHub.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load remote mirrors, using defaults. Error: {ex.Message}");
        }
        UpdateCustomWorker();
        _mirrorsLoaded = true;
    }

    public static void UpdateCustomWorker()
    {
        var customWorker = ConfigManager.Settings.CustomWorkerUrl?.TrimEnd('/');
        
        // Remove old custom worker if it exists
        _apiMirrors.RemoveAll(x => x != "https://7tv.io" && x != "https://api.7tv.app" && x != "https://eu.7tv.app");
        _cdnMirrors.RemoveAll(x => x != "https://cdn.7tv.app" && x != "https://cdn.zerotv.app");

        if (!string.IsNullOrWhiteSpace(customWorker))
        {
            if (!_apiMirrors.Contains(customWorker)) _apiMirrors.Insert(0, customWorker);
            if (!_cdnMirrors.Contains(customWorker)) _cdnMirrors.Insert(0, customWorker);
        }
    }

    public static List<string> GetApiMirrors() => _apiMirrors;
    public static List<string> GetCdnMirrors() => _cdnMirrors;

    public static HttpClient GetClient() => _httpClient;
}
