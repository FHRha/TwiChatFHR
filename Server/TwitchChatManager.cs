using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TwitchChatCore.Core;
using TwitchChatCore.Core.Models;

namespace TwitchChatCore.Server;

public class TwitchChatManager
{
    private CloudProxyServer? _activeProxy;
    private Stopwatch _proxyStopwatch = new Stopwatch();
    private CancellationTokenSource _timerCts = new CancellationTokenSource();
    
    public event Action<CloudProxyServer?>? ActiveProxyChanged;
    public event Action? ProxyUsageUpdated;

    public TwitchChatManager()
    {
        CheckAndResetMonthlyQuotas();
        StartUsageTimer();
    }

    private void CheckAndResetMonthlyQuotas()
    {
        var now = DateTime.UtcNow;
        if (now.Month != ConfigManager.Settings.LastQuotaResetDate.Month || 
            now.Year != ConfigManager.Settings.LastQuotaResetDate.Year)
        {
            foreach (var proxy in ConfigManager.Settings.CloudProxies)
            {
                proxy.UsageSeconds = 0;
            }
            ConfigManager.Settings.LastQuotaResetDate = now;
            ConfigManager.Save();
        }
    }

    private void StartUsageTimer()
    {
        Task.Run(async () =>
        {
            while (!_timerCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (_activeProxy != null && _proxyStopwatch.IsRunning)
                {
                    _activeProxy.UsageSeconds++;
                    // Save to disk every 60 seconds to avoid IO spam
                    if (_activeProxy.UsageSeconds % 60 == 0)
                    {
                        ConfigManager.Save();
                    }
                    ProxyUsageUpdated?.Invoke();
                }
            }
        });
    }
    
    public void StopActiveProxyTimer()
    {
        _proxyStopwatch.Stop();
        _activeProxy = null;
        ActiveProxyChanged?.Invoke(null);
        ConfigManager.Save();
    }

    public async Task<ClientWebSocket> ConnectAsync(CancellationToken cancellationToken)
    {
        StopActiveProxyTimer();
        CheckAndResetMonthlyQuotas();

        // 1. Direct connection attempt (Fastest, no limits, reveals IP)
        try
        {
            Console.WriteLine("TwitchChatManager: Trying direct connection to Twitch...");
            var directWs = new ClientWebSocket();
            await directWs.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), cancellationToken);
            Console.WriteLine("TwitchChatManager: Direct connection successful.");
            return directWs;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TwitchChatManager: Direct connection failed: {ex.Message}");
        }

        // 2. Fallback to Cloud Proxies if enabled and direct connection failed
        if (ConfigManager.Settings.UseTwitchProxy)
        {
            var availableProxies = ConfigManager.Settings.CloudProxies
                .Where(p => p.IsEnabled && p.UsageSeconds < 360000)
                .ToList();

            foreach (var proxy in availableProxies)
            {
                try
                {
                    Console.WriteLine($"TwitchChatManager: Trying proxy {proxy.Name} ({proxy.Url})...");
                    var proxyWs = new ClientWebSocket();
                    proxyWs.Options.SetRequestHeader("X-Proxy-Token", proxy.Token);
                    
                    var uri = new Uri(proxy.Url);
                    await proxyWs.ConnectAsync(uri, cancellationToken);
                    
                    Console.WriteLine($"TwitchChatManager: Connected via proxy {proxy.Name}.");
                    
                    _activeProxy = proxy;
                    _proxyStopwatch.Restart();
                    ActiveProxyChanged?.Invoke(_activeProxy);
                    
                    return proxyWs;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TwitchChatManager: Proxy {proxy.Name} failed: {ex.Message}");
                }
            }
        }

        throw new Exception("Все попытки подключения к Twitch Chat исчерпаны (напрямую и через прокси).");
    }
}
