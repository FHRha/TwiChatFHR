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

    public static void LoadLanguage(string lang)
    {
        var dict = new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://TwitchChatCore/App.axaml"))
        {
            Source = new Uri($"avares://TwitchChatCore/Resources/Lang.{lang}.axaml")
        };

        if (Current?.Resources?.MergedDictionaries != null && Current.Resources.MergedDictionaries.Count > 0)
        {
            Current.Resources.MergedDictionaries[0] = dict;
        }
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            TwitchChatCore.Core.ConfigManager.Load();
            LoadLanguage(TwitchChatCore.Core.ConfigManager.Settings.Language);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Views.MainWindow();

                LocalServer = new LocalServerManager();
                await LocalServer.StartAsync();
                
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