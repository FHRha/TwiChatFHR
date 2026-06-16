using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using TwitchChatCore.Core;

namespace TwitchChatCore.Server;

public class TwitchIrcClient
{
    private readonly ChatHub _chatHub;
    private readonly BadgeManager _badgeManager;
    private readonly EmoteManager _emoteManager;
    private ClientWebSocket? _webSocket;
    private string _channel = string.Empty;
    private string _currentRoomId = string.Empty;

    private static readonly string[] DefaultColors = new[]
    {
        "#FF0000", "#0000FF", "#008000", "#B22222", "#FF7F50", 
        "#9ACD32", "#FF4500", "#2E8B57", "#DAA520", "#D2691E", 
        "#5F9EA0", "#1E90FF", "#FF69B4", "#8A2BE2", "#00FF7F"
    };

    public TwitchIrcClient(ChatHub chatHub, BadgeManager badgeManager, EmoteManager emoteManager)
    {
        _chatHub = chatHub;
        _badgeManager = badgeManager;
        _emoteManager = emoteManager;
        _channel = ConfigManager.Settings.TwitchChannel;
    }

    public void SetChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.Equals(_channel, channel, StringComparison.OrdinalIgnoreCase))
            return;

        var oldChannel = _channel;
        _channel = channel.ToLower();

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            _ = SendAsync($"PART #{oldChannel}");
            _ = SendAsync($"JOIN #{_channel}");
            Console.WriteLine($"TwitchIrcClient: Switched channel from {oldChannel} to {_channel}");
        }
        
        _currentRoomId = string.Empty; // Reset so emotes reload
        _ = _badgeManager.LoadChannelBadgesAsync(_channel);
    }

    public async Task ConnectAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_channel))
                _channel = "stintik";

            _ = Task.Run(async () => 
            {
                var t1 = _badgeManager.LoadGlobalBadgesAsync();
                var t2 = _badgeManager.LoadChannelBadgesAsync(_channel);
                var t3 = _emoteManager.LoadGlobalEmotesAsync();
                await Task.WhenAll(t1, t2, t3);
            });

            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), CancellationToken.None);
            
            await SendAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
            await SendAsync("PASS SCHMOOPIIE");
            await SendAsync($"NICK justinfan{new Random().Next(10000, 99999)}");
            await SendAsync($"JOIN #{_channel}");

            Console.WriteLine($"TwitchIrcClient: Connected to channel {_channel}");

            _ = ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Twitch IRC Connection Error: {ex.Message}");
            // Optional: Reconnection logic
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        var stringBuilder = new StringBuilder();

        try
        {
            while (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                stringBuilder.Append(chunk);

                var message = stringBuilder.ToString();
                if (message.Contains("\r\n"))
                {
                    var lines = message.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    // The last item might be incomplete (if it doesn't end with \r\n)
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (line.StartsWith("PING"))
                        {
                            await SendAsync("PONG :tmi.twitch.tv");
                        }
                        else if (line.Contains(" PRIVMSG "))
                        {
                            await ParseAndBroadcastMessageAsync(line);
                        }
                    }

                    // Keep the last incomplete part in the builder
                    stringBuilder.Clear();
                    stringBuilder.Append(lines[lines.Length - 1]);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Receive Error: {ex.Message}");
        }
    }

    private async Task ParseAndBroadcastMessageAsync(string line)
    {
        try
        {
            // @badge-info=...;color=#0D4299;display-name=stintik;... :username!username@... PRIVMSG #channel :Hello!
            string color = "";
            string displayName = "";
            string text = "";
            var badgeUrls = new System.Collections.Generic.List<string>();

            if (line.StartsWith("@"))
            {
                var tagsEnd = line.IndexOf(' ');
                var tags = line.Substring(1, tagsEnd - 1);

                var tagParts = tags.Split(';');
                foreach (var tag in tagParts)
                {
                    if (tag.StartsWith("color=") && tag.Length > 6) color = tag.Substring(6);
                    if (tag.StartsWith("display-name=") && tag.Length > 13) displayName = tag.Substring(13);
                    if (tag.StartsWith("room-id=") && tag.Length > 8)
                    {
                        var roomId = tag.Substring(8);
                        if (roomId != _currentRoomId)
                        {
                            _currentRoomId = roomId;
                            _ = _emoteManager.LoadChannelEmotesAsync(_currentRoomId, _channel);
                        }
                    }
                    if (tag.StartsWith("badges=") && tag.Length > 7)
                    {
                        var badgesStr = tag.Substring(7);
                        var badgesList = badgesStr.Split(',');
                        foreach (var b in badgesList)
                        {
                            var url = _badgeManager.GetBadgeUrl(b);
                            if (url != null)
                            {
                                badgeUrls.Add(url);
                            }
                        }
                    }
                }

                var userStart = line.IndexOf(':', tagsEnd) + 1;
                var userEnd = line.IndexOf('!', userStart);
                if (string.IsNullOrEmpty(displayName) && userEnd > userStart)
                {
                    displayName = line.Substring(userStart, userEnd - userStart);
                }

                var msgStart = line.IndexOf(" PRIVMSG ", userEnd);
                msgStart = line.IndexOf(':', msgStart) + 1;
                if (msgStart > 0 && msgStart < line.Length)
                {
                    text = line.Substring(msgStart);
                }
            }
            else
            {
                // No tags
                var userStart = line.IndexOf(':') + 1;
                var userEnd = line.IndexOf('!', userStart);
                if (userEnd > userStart)
                {
                    displayName = line.Substring(userStart, userEnd - userStart);
                }

                var msgStart = line.IndexOf(" PRIVMSG ", userEnd);
                msgStart = line.IndexOf(':', msgStart) + 1;
                if (msgStart > 0 && msgStart < line.Length)
                {
                    text = line.Substring(msgStart);
                }
            }

            if (string.IsNullOrEmpty(displayName)) displayName = "Unknown";
            if (string.IsNullOrEmpty(color)) color = GetDefaultColor(displayName);

            var encodedText = System.Net.WebUtility.HtmlEncode(text);
            var htmlText = _emoteManager.ReplaceEmotes(encodedText, ConfigManager.Settings.SevenTVEmotesMode);

            var chatMessage = new
            {
                Username = displayName,
                Color = color,
                Text = text,
                TextHtml = htmlText,
                Badges = badgeUrls
            };

            var json = JsonSerializer.Serialize(chatMessage);
            await _chatHub.BroadcastMessageAsync(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parse Error: {ex.Message} on line: {line}");
        }
    }

    private async Task SendAsync(string message)
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\r\n");
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    private string GetDefaultColor(string username)
    {
        if (string.IsNullOrEmpty(username)) return "#FFFFFF";
        int hash = 0;
        foreach (char c in username)
        {
            hash = c + (hash << 5) - hash;
        }
        return DefaultColors[Math.Abs(hash) % DefaultColors.Length];
    }
}
