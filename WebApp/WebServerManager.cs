using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TwitchChatCore.Core;

namespace TwitchChatCore.Server;

/// <summary>
/// Web-oriented server manager for the WebApp project.
/// Adds REST API, auth middleware, and proper static routing for admin/ and overlay/.
/// </summary>
public class LocalServerManager
{
    private WebApplication? _app;
    public WebApplication? App => _app;
    public string BaseUrl { get; private set; } = string.Empty;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(async () =>
        {
            var builder = WebApplication.CreateBuilder();

            // Services
            builder.Services.AddSingleton<ChatHub>();
            builder.Services.AddSingleton<BadgeManager>();
            builder.Services.AddSingleton<EmoteManager>();
            builder.Services.AddSingleton<TwitchChatManager>();
            builder.Services.AddSingleton<TwitchIrcClient>();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(opts =>
            {
                opts.IdleTimeout = TimeSpan.FromHours(24);
                opts.Cookie.HttpOnly = true;
                opts.Cookie.IsEssential = true;
                opts.Cookie.SameSite = SameSiteMode.Lax;
            });

            // Listen on all interfaces so Docker/HuggingFace can reach it
            int port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 7860;
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(System.Net.IPAddress.Any, port);
            });

            _app = builder.Build();

            _app.UseSession();

            // ── WebSockets ────────────────────────────────────────────────────
            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            _app.Map("/ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var ws = await context.WebSockets.AcceptWebSocketAsync();
                    var hub = context.RequestServices.GetRequiredService<ChatHub>();
                    await hub.HandleConnectionAsync(ws);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            });

            // ── Health check (public) ─────────────────────────────────────────
            _app.MapGet("/health", context =>
            {
                context.Response.ContentType = "text/plain";
                return context.Response.WriteAsync("OK");
            });

            // ── Image cache proxy (public, needed by OBS overlay) ─────────────
            _app.MapGet("/cache/image", async context =>
            {
                var url = context.Request.Query["url"].ToString();
                if (string.IsNullOrEmpty(url)) { context.Response.StatusCode = 400; return; }

                var cacheDir = Path.Combine(ConfigManager.DataDir, "images");
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                string ext = url.Contains(".gif", StringComparison.OrdinalIgnoreCase) ? ".gif"
                    : url.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
                    : ".png";

                var safeFilename = Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
                    .Replace('/', '_').Replace('+', '-').Replace('=', '.') + ext;
                var filePath = Path.Combine(cacheDir, safeFilename);

                if (File.Exists(filePath)) { await context.Response.SendFileAsync(filePath); return; }

                try
                {
                    var bytes = await NetworkManager.GetClient().GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(filePath, bytes);
                    await context.Response.SendFileAsync(filePath);
                }
                catch { context.Response.StatusCode = 500; }
            });

            // ── Auth API endpoints (login / logout / setup) ───────────────────

            // GET /api/auth/status — returns whether setup is needed
            _app.MapGet("/api/auth/status", (HttpContext ctx) =>
            {
                bool configured = AdminAuth.IsConfigured(ConfigManager.Settings.AdminPasswordHash);
                bool loggedIn = ctx.Session.GetString("auth") == "ok";
                return Results.Json(new { configured, loggedIn });
            });

            // POST /api/auth/setup — first-run: set username+password
            _app.MapPost("/api/auth/setup", async (HttpContext ctx) =>
            {
                if (AdminAuth.IsConfigured(ConfigManager.Settings.AdminPasswordHash))
                    return Results.Forbid();

                var body = await JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body);
                if (body == null || string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 4)
                    return Results.BadRequest(new { error = "Password too short (min 4 chars)" });

                ConfigManager.Settings.AdminUsername = body.Username ?? "admin";
                ConfigManager.Settings.AdminPasswordHash = AdminAuth.HashPassword(body.Password);
                ConfigManager.Save();

                ctx.Session.SetString("auth", "ok");
                return Results.Ok(new { ok = true });
            });

            // POST /api/auth/login
            _app.MapPost("/api/auth/login", async (HttpContext ctx) =>
            {
                var body = await JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body);
                if (body == null) return Results.BadRequest();

                bool ok = AdminAuth.VerifyPassword(
                    body.Password ?? "",
                    ConfigManager.Settings.AdminPasswordHash);

                if (ok)
                {
                    ctx.Session.SetString("auth", "ok");
                    return Results.Ok(new { ok = true });
                }

                await Task.Delay(500); // Slow down brute force
                return Results.Json(new { ok = false, error = "Invalid credentials" },
                    statusCode: 401);
            });

            // POST /api/auth/logout
            _app.MapPost("/api/auth/logout", (HttpContext ctx) =>
            {
                ctx.Session.Clear();
                return Results.Ok();
            });

            // ── Protected API endpoints ───────────────────────────────────────

            // GET /api/config — return current AppSettings JSON
            _app.MapGet("/api/config", (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                return Results.Json(ConfigManager.Settings, _jsonOpts);
            });

            // POST /api/config — save new config and push ConfigUpdate to overlay
            _app.MapPost("/api/config", async (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();

                AppSettings? newSettings;
                try
                {
                    newSettings = await JsonSerializer.DeserializeAsync<AppSettings>(
                        ctx.Request.Body, _jsonOpts);
                }
                catch
                {
                    return Results.BadRequest(new { error = "Invalid JSON" });
                }

                if (newSettings == null) return Results.BadRequest();

                // Preserve auth credentials — they are set separately
                newSettings.AdminUsername = ConfigManager.Settings.AdminUsername;
                newSettings.AdminPasswordHash = ConfigManager.Settings.AdminPasswordHash;

                ConfigManager.UpdateSettings(newSettings);

                NetworkManager.UpdateCustomWorker();

                // Broadcast ConfigUpdate to all overlay WebSocket clients
                var hub = ctx.RequestServices.GetRequiredService<ChatHub>();
                await hub.BroadcastConfigAsync(ConfigManager.Settings);

                // Tell IRC client about channel/emote changes
                var irc = ctx.RequestServices.GetRequiredService<TwitchIrcClient>();
                irc.SetChannel(ConfigManager.Settings.TwitchChannel);

                return Results.Ok(new { ok = true });
            });

            // GET /api/status — connection status
            _app.MapGet("/api/status", (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                var irc = ctx.RequestServices.GetRequiredService<TwitchIrcClient>();
                return Results.Json(new
                {
                    channel = ConfigManager.Settings.TwitchChannel,
                    connected = irc.IsConnected,
                    activeProxy = irc.ActiveProxyName
                });
            });

            // POST /api/reconnect — force reconnect
            _app.MapPost("/api/reconnect", async (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                var irc = ctx.RequestServices.GetRequiredService<TwitchIrcClient>();
                await irc.ReconnectAsync();
                return Results.Ok(new { ok = true });
            });

            // GET /api/blacklist — get blacklist contents
            _app.MapGet("/api/blacklist", (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                var path = Path.Combine(ConfigManager.DataDir, "blacklist.txt");
                var content = File.Exists(path) ? File.ReadAllText(path) : "";
                return Results.Text(content);
            });

            // POST /api/blacklist — save blacklist
            _app.MapPost("/api/blacklist", async (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                using var reader = new StreamReader(ctx.Request.Body);
                var content = await reader.ReadToEndAsync();
                var path = Path.Combine(ConfigManager.DataDir, "blacklist.txt");
                await File.WriteAllTextAsync(path, content);
                return Results.Ok(new { ok = true });
            });

            // ── Static files for overlay (PUBLIC — OBS needs no auth) ─────────
            var overlayPath = Path.Combine(AppContext.BaseDirectory, "overlay");
            if (!Directory.Exists(overlayPath)) Directory.CreateDirectory(overlayPath);

            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(overlayPath),
                RequestPath = "/overlay"
            });

            // Emote image cache
            var emotesPath = Path.Combine(ConfigManager.DataDir, "emotes");
            if (!Directory.Exists(emotesPath)) Directory.CreateDirectory(emotesPath);

            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(emotesPath),
                RequestPath = "/cache/emotes"
            });

            // ── Overlay route: /overlay → overlay/index.html ──────────────────
            _app.MapGet("/overlay", async context =>
            {
                var file = Path.Combine(AppContext.BaseDirectory, "overlay", "index.html");
                if (File.Exists(file))
                    await context.Response.SendFileAsync(file);
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Overlay not found");
                }
            });

            // ── Admin static files (protected at page level via JS redirect) ──
            var adminPath = Path.Combine(AppContext.BaseDirectory, "admin");
            if (!Directory.Exists(adminPath)) Directory.CreateDirectory(adminPath);

            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(adminPath),
                RequestPath = ""
            });

            // ── Root: redirect to admin panel ─────────────────────────────────
            _app.MapGet("/", async context =>
            {
                var file = Path.Combine(AppContext.BaseDirectory, "admin", "index.html");
                if (File.Exists(file))
                    await context.Response.SendFileAsync(file);
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Admin panel not found");
                }
            });

            // Start the server
            await _app.StartAsync(cancellationToken);

            var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>();
            BaseUrl = addresses?.Addresses.FirstOrDefault() ?? $"http://0.0.0.0:{port}";

            Console.WriteLine($"Server started: {BaseUrl}");

            // Start Twitch IRC
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

    private static bool IsAuthorized(HttpContext ctx) =>
        ctx.Session.GetString("auth") == "ok";

    private record LoginRequest(string? Username, string? Password);
}
