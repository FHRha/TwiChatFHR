using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatCore.Server;

public class EmoteManager
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string> _globalEmotes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _channelEmotes = new(StringComparer.Ordinal);
    
    // Limits concurrent downloads so we don't spam 7TV CDN or kill the proxy
    private readonly SemaphoreSlim _downloadSemaphore = new(24, 24);

    public EmoteManager()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TwitchChatCore/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    private async Task<string> FetchWithMirrorFallbackAsync(string endpoint)
    {
        var mirrors = TwitchChatCore.Core.NetworkManager.GetApiMirrors();
        Exception? lastEx = null;
        foreach (var mirror in mirrors)
        {
            var url = $"{mirror}{endpoint}";
            if (mirror.Contains("script.google.com"))
            {
                var cleanMirror = mirror;
                if (mirror.Contains("?url=")) cleanMirror = mirror.Substring(0, mirror.IndexOf("?url="));
                url = $"{cleanMirror}?url={Uri.EscapeDataString("https://api.7tv.app" + endpoint)}";
            }
            try
            {
                return await TwitchChatCore.Core.NetworkManager.GetClient().GetStringAsync(url);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                var inner = ex.InnerException != null ? $" Inner: {ex.InnerException.Message}" : "";
                Console.WriteLine($"Mirror fallback: failed to fetch {url}: {ex.Message}{inner}");
            }
        }
        throw lastEx ?? new Exception("All 7TV API mirrors failed.");
    }

    public async Task LoadGlobalEmotesAsync()
    {
        try
        {
            await TwitchChatCore.Core.NetworkManager.LoadMirrorsAsync(); // Ensure mirrors are loaded

            var cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "metadata");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, "global_emotes.json");

            bool loadedFromCache = false;
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    await ParseAndDownloadEmotesAsync(json, _globalEmotes, "global");
                    loadedFromCache = true;
                    Console.WriteLine($"Loaded {_globalEmotes.Count} global emotes from cache.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cache corrupted for global emotes, ignoring cache. Error: {ex.Message}");
                    File.Delete(cacheFile);
                }
            }

            if (!loadedFromCache || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays >= 7)
            {
                Console.WriteLine("Fetching 7TV global emotes updates...");
                var json = await FetchWithMirrorFallbackAsync("/v3/emote-sets/global");
                
                await File.WriteAllTextAsync(cacheFile, json);
                await ParseAndDownloadEmotesAsync(json, _globalEmotes, "global");
                Console.WriteLine($"Updated {_globalEmotes.Count} global emotes.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load global 7TV emotes: {ex.Message}");
            OnEmoteDownloadError?.Invoke("global", ex.Message);
        }
    }

    public async Task LoadChannelEmotesAsync(string twitchUserId, string channelName)
    {
        try
        {
            await TwitchChatCore.Core.NetworkManager.LoadMirrorsAsync(); // Ensure mirrors are loaded

            var cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "metadata");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, $"channel_{channelName}_emotes.json");

            bool loadedFromCache = false;
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    _channelEmotes.Clear();
                    await ParseAndDownloadEmotesAsync(json, _channelEmotes, channelName);
                    loadedFromCache = true;
                    Console.WriteLine($"Loaded {_channelEmotes.Count} channel emotes from cache.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cache corrupted for channel {channelName}, ignoring cache. Error: {ex.Message}");
                    File.Delete(cacheFile);
                }
            }

            if (!loadedFromCache || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays > 1)
            {
                Console.WriteLine($"Fetching 7TV channel emotes updates for {channelName}...");
                var userJson = await FetchWithMirrorFallbackAsync($"/v3/users/twitch/{twitchUserId}");
                
                using var doc = JsonDocument.Parse(userJson);
                if (doc.RootElement.TryGetProperty("emote_set", out var emoteSet) && 
                    emoteSet.ValueKind == JsonValueKind.Object)
                {
                    var emoteSetJson = emoteSet.GetRawText();
                    await File.WriteAllTextAsync(cacheFile, emoteSetJson);
                    
                    _channelEmotes.Clear();
                    await ParseAndDownloadEmotesAsync(emoteSetJson, _channelEmotes, channelName);
                    Console.WriteLine($"Updated {_channelEmotes.Count} channel emotes.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load channel 7TV emotes for {channelName}: {ex.Message}");
            OnEmoteDownloadError?.Invoke(channelName, ex.Message);
        }
    }

    public static event Action<string, int, int, int, double>? OnEmoteDownloadProgress;
    public static event Action<string, string>? OnEmoteDownloadError;

    private async Task ParseAndDownloadEmotesAsync(string json, ConcurrentDictionary<string, string> dictionary, string folderName)
    {
        var emotesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "emotes", folderName);
        if (!Directory.Exists(emotesDir)) Directory.CreateDirectory(emotesDir);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("emotes", out var emotesList) && emotesList.ValueKind == JsonValueKind.Array)
        {
            var emotesToDownload = new List<(string url, string path)>();

            foreach (var emote in emotesList.EnumerateArray())
            {
                var name = emote.GetProperty("name").GetString();
                if (emote.TryGetProperty("data", out var data) && data.TryGetProperty("host", out var host))
                {
                    var urlBase = host.GetProperty("url").GetString();
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(urlBase))
                    {
                        if (urlBase.StartsWith("//")) urlBase = "https:" + urlBase;
                        bool isAnimated = false;
                        if (data.TryGetProperty("animated", out var animElement) && animElement.ValueKind == JsonValueKind.True)
                        {
                            isAnimated = true;
                        }

                        string fileName = "1x.webp";
                        string extension = ".webp";
                        
                        if (host.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
                        {
                            string? webpName = null, gifName = null, pngName = null;
                            foreach (var file in filesElement.EnumerateArray())
                            {
                                if (file.TryGetProperty("name", out var nameProp))
                                {
                                    var fName = nameProp.GetString();
                                    if (fName != null && fName.StartsWith("1x."))
                                    {
                                        if (fName.EndsWith(".webp")) webpName = fName;
                                        else if (fName.EndsWith(".gif")) gifName = fName;
                                        else if (fName.EndsWith(".png")) pngName = fName;
                                    }
                                }
                            }
                            
                            if (isAnimated && gifName != null) { fileName = gifName; extension = ".gif"; }
                            else if (!isAnimated && pngName != null && webpName == null) { fileName = pngName; extension = ".png"; }
                            else if (webpName != null) { fileName = webpName; extension = ".webp"; }
                            else if (gifName != null) { fileName = gifName; extension = ".gif"; }
                            else if (pngName != null) { fileName = pngName; extension = ".png"; }
                        }

                        var imgUrl = $"{urlBase}/{fileName}";
                        var emoteId = data.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                        
                        var localFileName = $"{emoteId}{extension}";
                        var localFilePath = Path.Combine(emotesDir, localFileName);
                        var routeUrl = $"/cache/emotes/{folderName}/{localFileName}";

                        dictionary[name] = routeUrl;

                        if (!File.Exists(localFilePath))
                        {
                            emotesToDownload.Add((imgUrl, localFilePath));
                        }
                    }
                }
            }

            if (emotesToDownload.Count > 0)
            {
                int total = emotesToDownload.Count;
                int processed = 0;
                int successful = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                OnEmoteDownloadProgress?.Invoke(folderName, 0, 0, total, 0);

                var downloadTasks = new List<Task>();
                foreach (var emote in emotesToDownload)
                {
                    downloadTasks.Add(Task.Run(async () => {
                        bool ok = await DownloadEmoteImageAsync(emote.url, emote.path);
                        int p = Interlocked.Increment(ref processed);
                        if (ok) Interlocked.Increment(ref successful);
                        double speed = p / stopwatch.Elapsed.TotalSeconds;
                        OnEmoteDownloadProgress?.Invoke(folderName, p, successful, total, speed);
                    }));
                }

                await Task.WhenAll(downloadTasks);
            }
            else
            {
                // Everything is cached, no downloads needed
                OnEmoteDownloadProgress?.Invoke(folderName, 0, 0, 0, 0);
            }
        }
    }

    private static byte[] DecodeIfBase64(byte[] data)
    {
        try
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            if (string.IsNullOrWhiteSpace(text)) return data;
            if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("[")) return data;

            byte[] decoded = Convert.FromBase64String(text);
            if (decoded.Length > 4)
            {
                if (decoded[0] == 'R' && decoded[1] == 'I' && decoded[2] == 'F' && decoded[3] == 'F') return decoded;
                if (decoded[0] == 'G' && decoded[1] == 'I' && decoded[2] == 'F' && decoded[3] == '8') return decoded;
                if (decoded[0] == 0x89 && decoded[1] == 0x50 && decoded[2] == 0x4E && decoded[3] == 0x47) return decoded;
            }
            return decoded; // Return decoded anyway if it's valid base64
        }
        catch { }
        return data;
    }

    private async Task<bool> DownloadEmoteImageAsync(string originalUrl, string localFilePath)
    {
        await _downloadSemaphore.WaitAsync();
        try
        {
            if (File.Exists(localFilePath)) return true;

            var pathAndQuery = new Uri(originalUrl).PathAndQuery;
            var mirrors = TwitchChatCore.Core.NetworkManager.GetCdnMirrors();
            
            foreach (var mirror in mirrors)
            {
                var url = $"{mirror}{pathAndQuery}";
                if (mirror.Contains("script.google.com"))
                {
                    var cleanMirror = mirror;
                    if (mirror.Contains("?url=")) cleanMirror = mirror.Substring(0, mirror.IndexOf("?url="));
                    url = $"{cleanMirror}?url={Uri.EscapeDataString(originalUrl)}";
                }
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        var bytes = await TwitchChatCore.Core.NetworkManager.GetClient().GetByteArrayAsync(url);
                        bytes = DecodeIfBase64(bytes);
                        await File.WriteAllBytesAsync(localFilePath, bytes);
                        return true; // Success
                    }
                    catch (Exception)
                    {

                        await Task.Delay(500); // Wait 500ms before retry
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading emote {originalUrl}: {ex.Message}");
            return false;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
        return false;
    }

    public string ReplaceEmotes(string encodedText, Core.SevenTVMode mode)
    {
        if (mode == Core.SevenTVMode.None || string.IsNullOrWhiteSpace(encodedText))
            return encodedText;

        // Using simple word replacement bounded by spaces to preserve HTML encoding
        var words = encodedText.Split(new[] { ' ' }, StringSplitOptions.None);
        
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            
            // Channel emotes take precedence
            if (_channelEmotes.TryGetValue(word, out var channelUrl))
            {
                words[i] = $"<span class=\"emote-container channel-emote\" data-text=\"{word}\"><img class=\"emote\" src=\"{channelUrl}\" alt=\"{word}\" /></span>";
                continue;
            }

            // Global emotes
            if (mode == Core.SevenTVMode.ChannelAndGlobal && _globalEmotes.TryGetValue(word, out var globalUrl))
            {
                words[i] = $"<span class=\"emote-container global-7tv-emote\" data-text=\"{word}\"><img class=\"emote\" src=\"{globalUrl}\" alt=\"{word}\" /></span>";
            }
        }

        return string.Join(" ", words);
    }
}
