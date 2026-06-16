using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatCore.Server;

public class ChatHub
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

    public async Task HandleConnectionAsync(WebSocket webSocket)
    {
        var socketId = Guid.NewGuid();
        _sockets.TryAdd(socketId, webSocket);

        // Send initial design config
        var initialConfig = $@"{{
            ""Type"": ""ConfigUpdate"",
            ""FontSize"": {TwitchChatCore.Core.ConfigManager.Settings.ChatFontSize},
            ""Spacing"": {TwitchChatCore.Core.ConfigManager.Settings.MessageSpacing},
            ""Opacity"": {TwitchChatCore.Core.ConfigManager.Settings.GlassOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            ""ShowStreamerEmotes"": {TwitchChatCore.Core.ConfigManager.Settings.ShowStreamerEmotes.ToString().ToLower()},
            ""ShowGlobalEmotes"": {TwitchChatCore.Core.ConfigManager.Settings.ShowGlobalEmotes.ToString().ToLower()},
            ""HideBackground"": {TwitchChatCore.Core.ConfigManager.Settings.HideBackground.ToString().ToLower()},
            ""HideBadges"": {TwitchChatCore.Core.ConfigManager.Settings.HideBadges.ToString().ToLower()},
            ""TextOutline"": {TwitchChatCore.Core.ConfigManager.Settings.TextOutline.ToString().ToLower()},
            ""TextColor"": ""{TwitchChatCore.Core.ConfigManager.Settings.CustomTextColor}""
        }}";
        var configBytes = Encoding.UTF8.GetBytes(initialConfig);
        await webSocket.SendAsync(new ArraySegment<byte>(configBytes), WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket error: {ex.Message}");
        }
        finally
        {
            _sockets.TryRemove(socketId, out _);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
    }

    public async Task BroadcastMessageAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var socket in _sockets.Values)
        {
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    // Ignore closed sockets
                }
            }
        }
    }
}
