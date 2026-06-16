using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TwitchChatCore.Server;

namespace TwitchChatCore;

public partial class App : Application
{
    public static LocalServerManager? LocalServer { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            TwitchChatCore.Core.ConfigManager.Load();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                LocalServer = new LocalServerManager();
                await LocalServer.StartAsync();

                desktop.MainWindow = new Views.MainWindow();
                
                desktop.Exit += async (s, e) => 
                {
                    if (LocalServer != null)
                    {
                        await LocalServer.StopAsync();
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash_app.log", ex.ToString());
            throw;
        }
    }
}