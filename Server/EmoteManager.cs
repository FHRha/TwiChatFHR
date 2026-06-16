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
    
    // Limits concurrent downloads so we don't spam 7TV CDN
    private readonly SemaphoreSlim _downloadSemaphore = new(10, 10);

    public EmoteManager()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TwitchChatCore/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task LoadGlobalEmotesAsync()
    {
        try
        {
            Console.WriteLine("Loading 7TV global emotes...");
            var json = await _httpClient.GetStringAsync("https://7tv.io/v3/emote-sets/global");
            await ParseAndDownloadEmotesAsync(json, _globalEmotes, "global");
            Console.WriteLine($"Loaded {_globalEmotes.Count} 7TV global emotes.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load global 7TV emotes: {ex.Message}");
        }
    }

    public async Task LoadChannelEmotesAsync(string twitchUserId, string channelName)
    {
        try
        {
            Console.WriteLine($"Loading 7TV channel emotes for {channelName}...");
            var json = await _httpClient.GetStringAsync($"https://7tv.io/v3/users/twitch/{twitchUserId}");
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("emote_set", out var emoteSet) && 
                emoteSet.ValueKind == JsonValueKind.Object)
            {
                var emoteSetJson = emoteSet.GetRawText();
                await ParseAndDownloadEmotesAsync(emoteSetJson, _channelEmotes, channelName);
                Console.WriteLine($"Loaded {_channelEmotes.Count} 7TV channel emotes for {channelName}.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load channel 7TV emotes for {channelName}: {ex.Message}");
        }
    }

    private async Task ParseAndDownloadEmotesAsync(string json, ConcurrentDictionary<string, string> dictionary, string folderName)
    {
        var emotesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "emotes", folderName);
        if (!Directory.Exists(emotesDir)) Directory.CreateDirectory(emotesDir);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("emotes", out var emotesList) && emotesList.ValueKind == JsonValueKind.Array)
        {
            var downloadTasks = new List<Task>();

            foreach (var emote in emotesList.EnumerateArray())
            {
                var name = emote.GetProperty("name").GetString();
                if (emote.TryGetProperty("data", out var data) && data.TryGetProperty("host", out var host))
                {
                    var urlBase = host.GetProperty("url").GetString();
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(urlBase))
                    {
                        if (urlBase.StartsWith("//")) urlBase = "https:" + urlBase;
                        var imgUrl = $"{urlBase}/1x.webp"; // Fetch 1x webp by default
                        var emoteId = data.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                        
                        var localFileName = $"{emoteId}.webp";
                        var localFilePath = Path.Combine(emotesDir, localFileName);
                        var routeUrl = $"/cache/emotes/{folderName}/{localFileName}";

                        // Add to dict pointing to local route
                        dictionary[name] = routeUrl;

                        // Download missing emotes to disk asynchronously
                        if (!File.Exists(localFilePath))
                        {
                            downloadTasks.Add(DownloadEmoteImageAsync(imgUrl, localFilePath));
                        }
                    }
                }
            }

            // Await all downloads for this set
            await Task.WhenAll(downloadTasks);
        }
    }

    private async Task DownloadEmoteImageAsync(string url, string localFilePath)
    {
        await _downloadSemaphore.WaitAsync();
        try
        {
            if (File.Exists(localFilePath)) return;
            var bytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localFilePath, bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading emote {url}: {ex.Message}");
        }
        finally
        {
            _downloadSemaphore.Release();
        }
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
                words[i] = $"<img class=\"chat-emote\" src=\"{channelUrl}\" alt=\"{word}\" />";
                continue;
            }

            // Global emotes
            if (mode == Core.SevenTVMode.ChannelAndGlobal && _globalEmotes.TryGetValue(word, out var globalUrl))
            {
                words[i] = $"<img class=\"chat-emote\" src=\"{globalUrl}\" alt=\"{word}\" />";
            }
        }

        return string.Join(" ", words);
    }
}
