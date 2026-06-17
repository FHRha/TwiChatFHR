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
    private CancellationTokenSource? _testModeCts;
    private int _testSpeedMs = 1000;

    public void SetTestSpeed(int ms)
    {
        _testSpeedMs = Math.Max(50, ms);
    }

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

        if (_channel == "test")
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _ = SendAsync($"PART #{oldChannel}");
                _webSocket.Abort();
                _webSocket.Dispose();
                _webSocket = null;
            }
            StartTestSimulator();
            return;
        }
        else
        {
            if (oldChannel == "test")
            {
                _testModeCts?.Cancel();
                _ = ConnectAsync();
                return;
            }
        }

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
                _channel = "test";

            if (_channel == "test")
            {
                StartTestSimulator();
                return;
            }

            _ = Task.Run(async () => 
            {
                var t1 = _badgeManager.LoadGlobalBadgesAsync();
                var t2 = _badgeManager.LoadChannelBadgesAsync(_channel);
                var t3 = TwitchChatCore.Core.ConfigManager.Settings.ShowGlobal7TVEmotes 
                            ? _emoteManager.LoadGlobalEmotesAsync() 
                            : Task.CompletedTask;
                await Task.WhenAll(t1, t2, t3);
            });

            _webSocket = new ClientWebSocket();
            // We intentionally do NOT set the proxy here. 
            // Twitch Chat IRC connection always goes directly for fault tolerance.
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

    private void StartTestSimulator()
    {
        _testModeCts?.Cancel();
        _testModeCts = new CancellationTokenSource();
        var token = _testModeCts.Token;

        _ = Task.Run(async () =>
        {
            Console.WriteLine("TwitchIrcClient: Test Simulator Started");
            int msgIndex = 0;
            var testUsers = new[] { "Viewer1", "ModMan", "BroadcasterTest", "Viewer1", "VipUser", "Newbie" };
            
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_testSpeedMs, token);
                if (token.IsCancellationRequested) break;

                string user = testUsers[msgIndex % testUsers.Length];
                string color = GetDefaultColor(user);
                string text = $"This is test message #{msgIndex}.";
                string htmlText = text;
                string role = "none";
                bool isFirstMessage = false;
                bool isMention = false;
                var badges = new System.Collections.Generic.List<string>();

                switch (user)
                {
                    case "ModMan":
                        role = "mod";
                        color = "#10B981";
                        text = "Please follow the chat rules! kappa";
                        break;
                    case "BroadcasterTest":
                        role = "broadcaster";
                        color = "#F59E0B";
                        text = "Welcome to the stream everyone! Thanks for joining!";
                        break;
                    case "VipUser":
                        role = "vip";
                        color = "#EC4899";
                        text = "I have a shiny badge.";
                        break;
                    case "Newbie":
                        isFirstMessage = true;
                        text = "Hello! First time here. Are we playing something fun?";
                        break;
                    case "Viewer1":
                        if (msgIndex % 3 == 0)
                        {
                            text = "@test How are you doing today? Just wanted to say hi!";
                            isMention = true;
                        }
                        else
                        {
                            text = "LUL LUL LUL Just chatting!";
                        }
                        break;
                }

                if (msgIndex % 7 == 0)
                {
                    text = "This is a very long message designed to test the word wrap functionality of the chat overlay. If it does not wrap correctly, it might break the layout. We need to make sure the chat message container correctly handles long strings of text without spaces as well like AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
                }

                isMention = System.Text.RegularExpressions.Regex.IsMatch(text, $@"(^|\s)@test\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                htmlText = System.Net.WebUtility.HtmlEncode(text);
                htmlText = _emoteManager.ReplaceEmotes(htmlText, SevenTVMode.ChannelAndGlobal);

                var chatMessage = new
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = user,
                    Color = color,
                    Text = text,
                    TextHtml = htmlText,
                    Badges = badges,
                    Role = role,
                    IsFirstMessage = isFirstMessage,
                    IsMention = isMention
                };

                var json = JsonSerializer.Serialize(chatMessage);
                await _chatHub.BroadcastMessageAsync(json);

                msgIndex++;
            }
        });
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
                        else if (line.Contains(" CLEARMSG "))
                        {
                            await HandleClearMsgAsync(line);
                        }
                        else if (line.Contains(" CLEARCHAT "))
                        {
                            await HandleClearChatAsync(line);
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
            string messageId = "";
            string role = "none";
            bool isFirstMessage = false;
            var badgeUrls = new System.Collections.Generic.List<string>();
            var twitchEmotes = new System.Collections.Generic.List<(int Start, int End, string Id)>();

            if (line.StartsWith("@"))
            {
                var tagsEnd = line.IndexOf(' ');
                var tags = line.Substring(1, tagsEnd - 1);

                var tagParts = tags.Split(';');
                foreach (var tag in tagParts)
                {
                    if (tag.StartsWith("color=") && tag.Length > 6) color = tag.Substring(6);
                    if (tag.StartsWith("display-name=") && tag.Length > 13) displayName = tag.Substring(13);
                    if (tag.StartsWith("id=") && tag.Length > 3) messageId = tag.Substring(3);
                    if (tag.StartsWith("room-id=") && tag.Length > 8)
                    {
                        var roomId = tag.Substring(8);
                        if (roomId != _currentRoomId)
                        {
                            _currentRoomId = roomId;
                            if (TwitchChatCore.Core.ConfigManager.Settings.ShowStreamerEmotes)
                            {
                                _ = _emoteManager.LoadChannelEmotesAsync(_currentRoomId, _channel);
                            }
                        }
                    }
                    if (tag.StartsWith("badges=") && tag.Length > 7)
                    {
                        var badgesStr = tag.Substring(7);
                        if (badgesStr.Contains("broadcaster/")) role = "broadcaster";
                        else if (badgesStr.Contains("moderator/")) role = "mod";
                        else if (badgesStr.Contains("vip/")) role = "vip";

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
                    if (tag.StartsWith("emotes=") && tag.Length > 7)
                    {
                        // Format: emotes=25:0-4,12-16/1902:6-10
                        var emotesStr = tag.Substring(7);
                        var emoteGroups = emotesStr.Split('/');
                        foreach (var group in emoteGroups)
                        {
                            var parts = group.Split(':');
                            if (parts.Length == 2)
                            {
                                var emoteId = parts[0];
                                var ranges = parts[1].Split(',');
                                foreach (var range in ranges)
                                {
                                    var bounds = range.Split('-');
                                    if (bounds.Length == 2 && int.TryParse(bounds[0], out int start) && int.TryParse(bounds[1], out int end))
                                    {
                                        twitchEmotes.Add((start, end, emoteId));
                                    }
                                }
                            }
                        }
                    }
                    if (tag.StartsWith("first-msg=") && tag.Length > 10)
                    {
                        if (tag.Substring(10) == "1") isFirstMessage = true;
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

            string htmlText = text;

            // Always generate HTML for all emotes so frontend can toggle them dynamically via CSS
            if (twitchEmotes.Count > 0)
            {
                // Sort descending to not break indices when replacing
                twitchEmotes.Sort((a, b) => b.Start.CompareTo(a.Start));
                var builder = new StringBuilder();
                int lastIndex = text.Length;

                foreach (var em in twitchEmotes)
                {
                    // Safety check against surrogate issues or malformed data
                    if (em.End < lastIndex && em.Start >= 0 && em.End >= em.Start && em.End < text.Length)
                    {
                        var emoteText = text.Substring(em.Start, em.End - em.Start + 1);
                        var suffix = text.Substring(em.End + 1, lastIndex - em.End - 1);
                        var encodedSuffix = System.Net.WebUtility.HtmlEncode(suffix);
                        encodedSuffix = _emoteManager.ReplaceEmotes(encodedSuffix, SevenTVMode.ChannelAndGlobal);
                        builder.Insert(0, encodedSuffix);
                        
                        var originalUrl = $"https://static-cdn.jtvnw.net/emoticons/v2/{em.Id}/default/dark/1.0";
                        var proxyUrl = $"/cache/image?url={Uri.EscapeDataString(originalUrl)}";
                        
                        builder.Insert(0, $"<span class=\"emote-container twitch-emote\" data-text=\"{System.Net.WebUtility.HtmlEncode(emoteText)}\"><img class=\"emote\" src=\"{proxyUrl}\" alt=\"emote\" /></span>");
                        lastIndex = em.Start;
                    }
                }
                var prefix = text.Substring(0, lastIndex);
                var encodedPrefix = System.Net.WebUtility.HtmlEncode(prefix);
                encodedPrefix = _emoteManager.ReplaceEmotes(encodedPrefix, SevenTVMode.ChannelAndGlobal);
                builder.Insert(0, encodedPrefix);
                htmlText = builder.ToString();
            }
            else
            {
                htmlText = System.Net.WebUtility.HtmlEncode(text);
                htmlText = _emoteManager.ReplaceEmotes(htmlText, SevenTVMode.ChannelAndGlobal);
            }

            bool isMention = false;
            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(_channel))
            {
                isMention = System.Text.RegularExpressions.Regex.IsMatch(text, $@"(^|\s)@{System.Text.RegularExpressions.Regex.Escape(_channel)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var chatMessage = new
            {
                Id = messageId,
                Username = displayName,
                Color = color,
                Text = text,
                TextHtml = htmlText,
                Badges = badgeUrls,
                Role = role,
                IsFirstMessage = isFirstMessage,
                IsMention = isMention
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

    private async Task HandleClearMsgAsync(string line)
    {
        try
        {
            string targetMsgId = "";
            if (line.StartsWith("@"))
            {
                var tagsEnd = line.IndexOf(' ');
                var tags = line.Substring(1, tagsEnd - 1);
                var tagParts = tags.Split(';');
                foreach (var tag in tagParts)
                {
                    if (tag.StartsWith("target-msg-id=") && tag.Length > 14)
                    {
                        targetMsgId = tag.Substring(14);
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(targetMsgId))
            {
                var json = JsonSerializer.Serialize(new { Type = "ClearMessage", Id = targetMsgId });
                await _chatHub.BroadcastMessageAsync(json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClearMsg Error: {ex.Message}");
        }
    }

    private async Task HandleClearChatAsync(string line)
    {
        try
        {
            // @tags :tmi.twitch.tv CLEARCHAT #channel :username
            // or :tmi.twitch.tv CLEARCHAT #channel
            string targetUsername = "";
            var msgStart = line.IndexOf(" CLEARCHAT ");
            msgStart = line.IndexOf(':', msgStart + 11);
            if (msgStart > 0 && msgStart + 1 < line.Length)
            {
                targetUsername = line.Substring(msgStart + 1).Trim();
            }

            var json = JsonSerializer.Serialize(new { Type = "ClearChat", Username = targetUsername });
            await _chatHub.BroadcastMessageAsync(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClearChat Error: {ex.Message}");
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
