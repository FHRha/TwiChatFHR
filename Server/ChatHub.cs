using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TwitchChatCore.Server;

public class ChatClient
{
    public WebSocket Socket { get; }
    public Channel<string> MessageQueue { get; }
    public CancellationTokenSource Cts { get; }

    public ChatClient(WebSocket socket)
    {
        Socket = socket;
        // Bounded channel to prevent infinite memory growth if a client is completely frozen
        MessageQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
        Cts = new CancellationTokenSource();
    }
}

public partial class ChatHub
{
    private readonly ConcurrentDictionary<Guid, ChatClient> _clients = new();

    public ChatHub()
    {
    }

    public async Task HandleConnectionAsync(WebSocket webSocket)
    {
        var socketId = Guid.NewGuid();
        var client = new ChatClient(webSocket);
        _clients.TryAdd(socketId, client);

        // Start dedicated sender loop for this client
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in client.MessageQueue.Reader.ReadAllAsync(client.Cts.Token))
                {
                    if (webSocket.State != WebSocketState.Open) break;
                    var bytes = Encoding.UTF8.GetBytes(message);
                    var segment = new ArraySegment<byte>(bytes);
                    
                    try
                    {
                        using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(client.Cts.Token, sendCts.Token);
                        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, linkedCts.Token);
                    }
                    catch
                    {
                        // Ignore individual send errors, loop continues
                    }
                }
            }
            catch { }
        });

        // Send initial design config
        var initialConfig = $@"{{
            ""Type"": ""ConfigUpdate"",
            ""FontSize"": {TwitchChatCore.Core.ConfigManager.Settings.ChatFontSize},
            ""Spacing"": {TwitchChatCore.Core.ConfigManager.Settings.MessageSpacing},
            ""Opacity"": {TwitchChatCore.Core.ConfigManager.Settings.GlassOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            ""ShowStreamerEmotes"": {TwitchChatCore.Core.ConfigManager.Settings.ShowStreamerEmotes.ToString().ToLower()},
            ""ShowGlobalEmotes"": {TwitchChatCore.Core.ConfigManager.Settings.ShowGlobalEmotes.ToString().ToLower()},
            ""ShowGlobal7TVEmotes"": {TwitchChatCore.Core.ConfigManager.Settings.ShowGlobal7TVEmotes.ToString().ToLower()},
            ""HideBackground"": {TwitchChatCore.Core.ConfigManager.Settings.HideBackground.ToString().ToLower()},
            ""HideBadges"": {TwitchChatCore.Core.ConfigManager.Settings.HideBadges.ToString().ToLower()},
            ""EnableRoleColors"": {TwitchChatCore.Core.ConfigManager.Settings.EnableRoleColors.ToString().ToLower()},
            ""TextOutline"": {TwitchChatCore.Core.ConfigManager.Settings.TextOutline.ToString().ToLower()},
            ""TextColor"": ""{TwitchChatCore.Core.ConfigManager.Settings.CustomTextColor}"",
            ""ColorBroadcaster"": ""{TwitchChatCore.Core.ConfigManager.Settings.ColorBroadcaster}"",
            ""ColorMod"": ""{TwitchChatCore.Core.ConfigManager.Settings.ColorMod}"",
            ""ColorVip"": ""{TwitchChatCore.Core.ConfigManager.Settings.ColorVip}"",
            ""AnimationType"": ""{TwitchChatCore.Core.ConfigManager.Settings.AnimationType.ToString().ToLower()}"",
            ""EnableMessageGrouping"": {TwitchChatCore.Core.ConfigManager.Settings.EnableMessageGrouping.ToString().ToLower()},
            ""HighlightMentions"": {TwitchChatCore.Core.ConfigManager.Settings.HighlightMentions.ToString().ToLower()},
            ""HighlightFirstMessage"": {TwitchChatCore.Core.ConfigManager.Settings.HighlightFirstMessage.ToString().ToLower()},
            ""DesignTheme"": ""{TwitchChatCore.Core.ConfigManager.Settings.DesignTheme.ToString().ToLower()}"",
            ""DesignShape"": ""{TwitchChatCore.Core.ConfigManager.Settings.DesignShape.ToString().ToLower()}"",
            ""DesignLayout"": ""{TwitchChatCore.Core.ConfigManager.Settings.DesignLayout.ToString().ToLower()}""
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
            TwitchChatCore.Core.Logger.Log($"WebSocket error: {ex.Message}");
        }
        finally
        {
            client.Cts.Cancel();
            _clients.TryRemove(socketId, out _);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
    }

    public Task BroadcastMessageAsync(string message)
    {
        foreach (var client in _clients.Values)
        {
            client.MessageQueue.Writer.TryWrite(message);
        }
        return Task.CompletedTask;
    }
}
