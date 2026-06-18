using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace TwitchChatCore.Server;

public class CdnManager
{
    private static readonly string[] GitHubMirrors = new[]
    {
        "https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/Server/Resources/global_badges.json",
        "https://cdn.jsdelivr.net/gh/FHRha/TwiChatFHR@main/Server/Resources/global_badges.json",
        "https://ghproxy.com/https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/Server/Resources/global_badges.json",
        "https://raw.staticaly.com/gh/FHRha/TwiChatFHR/main/Server/Resources/global_badges.json"
    };

    private int _lastWorkingIndex = 0;
    
    // Short timeout for fast carousel switching


    public CdnManager()
    {
    }

    public async Task<string> DownloadGlobalBadgesAsync()
    {
        for (int i = 0; i < GitHubMirrors.Length; i++)
        {
            int index = (_lastWorkingIndex + i) % GitHubMirrors.Length;
            string url = GitHubMirrors[index];
            try
            {
                TwitchChatCore.Core.Logger.Log($"[CDN] Trying mirror {index}: {url}");
                var response = await TwitchChatCore.Core.NetworkManager.GetClient().GetStringAsync(url);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    if (_lastWorkingIndex != index)
                    {
                        TwitchChatCore.Core.Logger.Log($"[CDN] Switching cached working index to {index}");
                        _lastWorkingIndex = index;
                    }
                    return response;
                }
            }
            catch (Exception ex)
            {
                TwitchChatCore.Core.Logger.Log($"[CDN] Mirror {index} failed: {ex.Message}");
            }
        }
        
        throw new Exception("All GitHub CDN mirrors failed to respond. They might be blocked or currently unavailable.");
    }
}
