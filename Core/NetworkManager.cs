using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwitchChatCore.Core;

public static class NetworkManager
{
    private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler 
    { 
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        }
    }) 
    { Timeout = TimeSpan.FromSeconds(30) };
    private static List<string> _apiMirrors = new() { "https://7tv.io", "https://api.7tv.app" };
    private static List<string> _cdnMirrors = new() { "https://cdn.7tv.app", "https://cdn.zerotv.app" };
    private static bool _mirrorsLoaded = false;
    private static readonly object _mirrorsLock = new object();

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
                if (apis.Count > 0) 
                {
                    lock (_mirrorsLock) { _apiMirrors = apis; }
                }
            }

            if (doc.RootElement.TryGetProperty("cdn", out var cdnElement))
            {
                var cdns = new List<string>();
                foreach (var el in cdnElement.EnumerateArray()) cdns.Add(el.GetString()?.TrimEnd('/') ?? "");
                if (cdns.Count > 0) 
                {
                    lock (_mirrorsLock) { _cdnMirrors = cdns; }
                }
            }
            TwitchChatCore.Core.Logger.Log($"Loaded {_apiMirrors.Count} API mirrors and {_cdnMirrors.Count} CDN mirrors from GitHub.");
        }
        catch (Exception ex)
        {
            TwitchChatCore.Core.Logger.Log($"Failed to load remote mirrors, using defaults. Error: {ex.Message}");
        }
        _mirrorsLoaded = true;
        UpdateCustomWorker();
    }

    /// <summary>Reset mirror cache so the next LoadMirrorsAsync call re-applies proxy settings.</summary>
    public static void ResetMirrors()
    {
        _mirrorsLoaded = false;
    }

    public static void UpdateCustomWorker()
    {
        lock (_mirrorsLock)
        {
            // Reset to default
            _apiMirrors.Clear();
            _cdnMirrors.Clear();
            
            if (!ConfigManager.Settings.UseStrictEmoteProxy)
            {
                _apiMirrors.AddRange(new[] { "https://7tv.io", "https://api.7tv.app", "https://eu.7tv.app" });
                _cdnMirrors.AddRange(new[] { "https://cdn.7tv.app", "https://cdn.zerotv.app" });
            }

            if (ConfigManager.Settings.UseTwitchProxyForEmotes && ConfigManager.Settings.UseTwitchProxy && ConfigManager.Settings.CloudProxies.Count > 0)
            {
                var activeProxy = ConfigManager.Settings.CloudProxies.FirstOrDefault(p => p.IsEnabled);
                if (activeProxy != null)
                {
                    string baseUrl = activeProxy.Url;
                    if (baseUrl.StartsWith("wss://")) baseUrl = "https://" + baseUrl.Substring(6);
                    else if (baseUrl.StartsWith("ws://")) baseUrl = "http://" + baseUrl.Substring(5);
                    
                    baseUrl = baseUrl.TrimEnd('/');
                    string hfProxy = $"{baseUrl}/proxy?token={Uri.EscapeDataString(activeProxy.Token)}&url=";
                    _apiMirrors.Insert(0, hfProxy);
                    _cdnMirrors.Insert(0, hfProxy);
                }
            }
            else if (ConfigManager.Settings.UseCustomEmoteProxy)
            {
                var customWorker = ConfigManager.Settings.CustomWorkerUrl?.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(customWorker))
                {
                    if (!_apiMirrors.Contains(customWorker)) _apiMirrors.Insert(0, customWorker);
                    if (!_cdnMirrors.Contains(customWorker)) _cdnMirrors.Insert(0, customWorker);
                }
            }
        }
    }

    public static List<string> GetApiMirrors() 
    { 
        lock (_mirrorsLock) return new List<string>(_apiMirrors); 
    }
    public static List<string> GetCdnMirrors() 
    { 
        lock (_mirrorsLock) return new List<string>(_cdnMirrors); 
    }

    public static HttpClient GetClient() => _httpClient;
}
