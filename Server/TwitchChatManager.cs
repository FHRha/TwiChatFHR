using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TwitchChatCore.Core;
using TwitchChatCore.Core.Models;

namespace TwitchChatCore.Server;

public partial class TwitchChatManager
{
    private CloudProxyServer? _activeProxy;
    private CancellationTokenSource _timerCts = new CancellationTokenSource();
    
    public event Action<CloudProxyServer?>? ActiveProxyChanged;

    public TwitchChatManager()
    {
        StartHealthCheckTimer();
    }

    private void StartHealthCheckTimer()
    {
        Task.Run(async () =>
        {
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            while (!_timerCts.Token.IsCancellationRequested)
            {
                if (ConfigManager.Settings.UseTwitchProxy)
                {
                    foreach (var proxy in ConfigManager.Settings.CloudProxies.ToList())
                    {
                        if (string.IsNullOrWhiteSpace(proxy.Url)) continue;
                        
                        try
                        {
                            var httpUrl = proxy.Url.Replace("wss://", "https://").Replace("ws://", "http://");
                            var sw = Stopwatch.StartNew();
                            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, httpUrl);
                            var response = await httpClient.SendAsync(request, _timerCts.Token);
                            sw.Stop();
                            
                            if (response.IsSuccessStatusCode)
                            {
                                proxy.StatusText = $"OK: {sw.ElapsedMilliseconds} мс";
                                proxy.StatusColor = "#10B981"; // Green
                            }
                            else
                            {
                                proxy.StatusText = $"Ошибка: {(int)response.StatusCode}";
                                proxy.StatusColor = "#EF4444"; // Red
                            }
                        }
                        catch (Exception)
                        {
                            proxy.StatusText = "Оффлайн";
                            proxy.StatusColor = "#EF4444";
                        }
                    }
                }
                
                await Task.Delay(5000, _timerCts.Token);
            }
        });
    }

    public async Task<ClientWebSocket> ConnectAsync(CancellationToken cancellationToken)
    {
        _activeProxy = null;
        ActiveProxyChanged?.Invoke(null);

        // 1. Direct connection attempt (if not strict proxy)
        if (!ConfigManager.Settings.UseTwitchProxy || !ConfigManager.Settings.UseStrictTwitchProxy)
        {
            try
            {
                TwitchChatCore.Core.Logger.Log("TwitchChatManager: Trying direct connection to Twitch...");
                var directWs = new ClientWebSocket();
                await directWs.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), cancellationToken);
                TwitchChatCore.Core.Logger.Log("TwitchChatManager: Direct connection successful.");
                return directWs;
            }
            catch (Exception ex)
            {
                TwitchChatCore.Core.Logger.Log($"TwitchChatManager: Direct connection failed: {ex.Message}");
                if (!ConfigManager.Settings.UseTwitchProxy)
                {
                    throw new Exception("Не удалось подключиться к Twitch Chat напрямую.");
                }
            }
        }

        // 2. Cloud Proxies attempt
        if (ConfigManager.Settings.UseTwitchProxy)
        {
            var availableProxies = ConfigManager.Settings.CloudProxies
                .Where(p => p.IsEnabled)
                .ToList();

            foreach (var proxy in availableProxies)
            {
                try
                {
                    TwitchChatCore.Core.Logger.Log($"TwitchChatManager: Trying proxy {proxy.Name} ({proxy.Url})...");
                    var proxyWs = new ClientWebSocket();
                    proxyWs.Options.SetRequestHeader("X-Proxy-Token", proxy.Token);
                    
                    var uri = new Uri(proxy.Url);
                    await proxyWs.ConnectAsync(uri, cancellationToken);
                    
                    TwitchChatCore.Core.Logger.Log($"TwitchChatManager: Connected via proxy {proxy.Name}.");
                    
                    _activeProxy = proxy;
                    ActiveProxyChanged?.Invoke(_activeProxy);
                    
                    return proxyWs;
                }
                catch (Exception ex)
                {
                    TwitchChatCore.Core.Logger.Log($"TwitchChatManager: Proxy {proxy.Name} failed: {ex.Message}");
                }
            }
            
            throw new Exception("Не удалось подключиться ни напрямую, ни к одному прокси-серверу Twitch.");
        }

        throw new Exception("Все попытки подключения к Twitch Chat исчерпаны.");
    }
}
