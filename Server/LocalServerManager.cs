using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace TwitchChatCore.Server;

public class LocalServerManager
{
    private WebApplication? _app;
    public WebApplication? App => _app;
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(async () =>
        {
            var builder = WebApplication.CreateBuilder();

        // Register ChatHub as a singleton
        builder.Services.AddSingleton<ChatHub>();
        builder.Services.AddSingleton<BadgeManager>();
        builder.Services.AddSingleton<EmoteManager>();
        builder.Services.AddSingleton<TwitchChatManager>();
        builder.Services.AddSingleton<TwitchIrcClient>();

        // We tell Kestrel to use configured port, or 0 for auto
        builder.WebHost.ConfigureKestrel(options =>
        {
            int port = TwitchChatCore.Core.ConfigManager.Settings.ServerPort;
            options.Listen(System.Net.IPAddress.Loopback, port);
        });

        _app = builder.Build();

        // Setup WebSockets
        var webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        };
        _app.UseWebSockets(webSocketOptions);

        _app.Map("/ws", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var chatHub = context.RequestServices.GetRequiredService<ChatHub>();
                await chatHub.HandleConnectionAsync(webSocket);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        // Setup static files (browser frontend)
        var browserPath = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "browser");
        if (!Directory.Exists(browserPath))
        {
            Directory.CreateDirectory(browserPath);
        }

        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(browserPath),
            RequestPath = ""
        });

        // Setup static files for local emotes cache
        var emotesPath = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "cache", "emotes");
        if (!Directory.Exists(emotesPath))
        {
            Directory.CreateDirectory(emotesPath);
        }

        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(emotesPath),
            RequestPath = "/cache/emotes"
        });

        // Setup Image Cache Route (for badges and twitch emotes)
        _app.MapGet("/cache/image", async context =>
        {
            var url = context.Request.Query["url"].ToString();
            if (string.IsNullOrEmpty(url))
            {
                context.Response.StatusCode = 400;
                return;
            }

            var cacheDir = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "cache", "images");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string ext = ".png";
            if (url.Contains(".gif", StringComparison.OrdinalIgnoreCase)) ext = ".gif";
            else if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase)) ext = ".webp";

            // Create a safe, unique filename based on the URL
            var safeFilename = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                                .Replace('/', '_').Replace('+', '-').Replace('=', '.') + ext;
            var filePath = Path.Combine(cacheDir, safeFilename);

            if (File.Exists(filePath))
            {
                await context.Response.SendFileAsync(filePath);
                return;
            }

            try
            {
                var bytes = await TwitchChatCore.Core.NetworkManager.GetClient().GetByteArrayAsync(url);
                
                await File.WriteAllBytesAsync(filePath, bytes);

                // SendFileAsync will automatically set Content-Type based on extension
                await context.Response.SendFileAsync(filePath);
            }
            catch
            {
                context.Response.StatusCode = 500;
            }
        });

        // Map favicon
        _app.MapGet("/favicon.ico", async context =>
        {
            var faviconPath = Path.Combine(TwitchChatCore.Core.ConfigManager.AppDir, "Server", "Resources", "favicon.ico");
            if (File.Exists(faviconPath))
            {
                context.Response.ContentType = "image/x-icon";
                await context.Response.SendFileAsync(faviconPath);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });

        // Start the app without blocking
        await _app.StartAsync(cancellationToken);

        // Retrieve the dynamically assigned port
        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var serverAddressesFeature = server.Features.Get<IServerAddressesFeature>();
        BaseUrl = serverAddressesFeature?.Addresses.FirstOrDefault() ?? "http://127.0.0.1:8080";
        
        Console.WriteLine($"Local server started at: {BaseUrl}");
        
        // Start Twitch Client
        var twitchClient = _app.Services.GetRequiredService<TwitchIrcClient>();
        _ = twitchClient.ConnectAsync();
        });
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
