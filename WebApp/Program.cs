using TwitchChatCore.Core;
using TwitchChatCore.Server;

// Load configuration
ConfigManager.Load();

// Apply env-variable overrides (useful for Docker / HuggingFace Spaces)
var envChannel = Environment.GetEnvironmentVariable("TWITCH_CHANNEL");
if (!string.IsNullOrWhiteSpace(envChannel))
    ConfigManager.Settings.TwitchChannel = envChannel;

var envUser = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
if (!string.IsNullOrWhiteSpace(envUser))
    ConfigManager.Settings.AdminUsername = envUser;

var envPass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
if (!string.IsNullOrWhiteSpace(envPass))
    ConfigManager.Settings.AdminPasswordHash = AdminAuth.HashPassword(envPass);

ConfigManager.Save();

// Start the ASP.NET Core web server
var serverManager = new LocalServerManager();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await serverManager.StartAsync(cts.Token);

var port = Environment.GetEnvironmentVariable("PORT") ?? "7860";
Console.WriteLine($"\n========================================");
Console.WriteLine($"  TwiChatFHR Web is running!");
Console.WriteLine($"  Admin Panel : http://0.0.0.0:{port}/");
Console.WriteLine($"  Overlay URL : http://0.0.0.0:{port}/overlay");
Console.WriteLine($"========================================\n");

var waitForCancel = new TaskCompletionSource();
cts.Token.Register(() => waitForCancel.TrySetResult());
await waitForCancel.Task;
await serverManager.StopAsync();
