using System.Text;
using TwitchChatCore.Core;

namespace TwitchChatCore.Server;

/// <summary>
/// Web-specific extensions for TwitchIrcClient.
/// Adds IsConnected and ActiveProxyName properties needed by the REST API.
/// </summary>
public partial class TwitchIrcClient
{
    /// <summary>True when the WebSocket to Twitch (or proxy) is open.</summary>
    public bool IsConnected =>
        _webSocket?.State == System.Net.WebSockets.WebSocketState.Open;

    /// <summary>Name of the active cloud proxy, or null if using direct connection.</summary>
    public string? ActiveProxyName => _chatManager.ActiveProxy?.Name;
}

/// <summary>
/// Web-specific extensions for TwitchChatManager.
/// Exposes the active proxy for REST API status responses.
/// </summary>
public partial class TwitchChatManager
{
    public Core.Models.CloudProxyServer? ActiveProxy => _activeProxy;
}

/// <summary>
/// Web-specific extensions for ChatHub.
/// Adds BroadcastConfigAsync to push ConfigUpdate messages to overlay clients.
/// </summary>
public partial class ChatHub
{
    public Task BroadcastConfigAsync(AppSettings s)
    {
        string json = $@"{{
            ""Type"": ""ConfigUpdate"",
            ""FontSize"": {s.ChatFontSize},
            ""Spacing"": {s.MessageSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            ""Opacity"": {s.GlassOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            ""ShowStreamerEmotes"": {s.ShowStreamerEmotes.ToString().ToLower()},
            ""ShowGlobalEmotes"": {s.ShowGlobalEmotes.ToString().ToLower()},
            ""ShowGlobal7TVEmotes"": {s.ShowGlobal7TVEmotes.ToString().ToLower()},
            ""HideBackground"": {s.HideBackground.ToString().ToLower()},
            ""HideBadges"": {s.HideBadges.ToString().ToLower()},
            ""HideBotMessages"": {s.HideBotMessages.ToString().ToLower()},
            ""HideModMessages"": {s.HideModMessages.ToString().ToLower()},
            ""HideVipMessages"": {s.HideVipMessages.ToString().ToLower()},
            ""EnableRoleColors"": {s.EnableRoleColors.ToString().ToLower()},
            ""TextOutline"": {s.TextOutline.ToString().ToLower()},
            ""TextColor"": ""{s.CustomTextColor}"",
            ""MessageBgColor"": ""{s.MessageBgColor}"",
            ""GlobalBgColor"": ""{s.GlobalBgColor}"",
            ""ColorBroadcaster"": ""{s.ColorBroadcaster}"",
            ""ColorMod"": ""{s.ColorMod}"",
            ""ColorVip"": ""{s.ColorVip}"",
            ""AnimationType"": ""{s.AnimationType.ToString().ToLower()}"",
            ""EnableMessageGrouping"": {s.EnableMessageGrouping.ToString().ToLower()},
            ""HighlightMentions"": {s.HighlightMentions.ToString().ToLower()},
            ""HighlightFirstMessage"": {s.HighlightFirstMessage.ToString().ToLower()},
            ""DesignTheme"": ""{s.DesignTheme.ToString().ToLower()}"",
            ""BorderStyle"": ""{s.BorderStyle.ToString().ToLower()}"",
            ""DesignShape"": ""{s.DesignShape.ToString().ToLower()}"",
            ""DesignLayout"": ""{s.DesignLayout.ToString().ToLower()}"",
            ""Font"": ""{s.Font.ToString().ToLower()}""
        }}";
        return BroadcastMessageAsync(json);
    }
}
