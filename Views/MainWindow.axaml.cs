using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TwitchChatCore.Core;

namespace TwitchChatCore.Views;

public partial class MainWindow : Window
{
    private bool _isInitialized = false;

    public MainWindow()
    {
        InitializeComponent();

        // Load config to UI
        UsernameTextBox.Text = ConfigManager.Settings.TwitchChannel;
        ServerPortTextBox.Text = ConfigManager.Settings.ServerPort.ToString();
        
        var methodItem = MethodComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == ConfigManager.Settings.CircumventionMethod);
        if (methodItem != null) MethodComboBox.SelectedItem = methodItem;

        if (App.LocalServer != null)
        {
            ObsUrlTextBox.Text = App.LocalServer.BaseUrl + "/index.html";
        }
        
        _isInitialized = true;
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SaveSettings();
    }

    private void ComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (!_isInitialized) return;

        ConfigManager.Settings.TwitchChannel = UsernameTextBox.Text ?? "";
        if (int.TryParse(ServerPortTextBox.Text, out int port))
        {
            ConfigManager.Settings.ServerPort = port;
        }
        
        if (MethodComboBox.SelectedItem is ComboBoxItem item)
        {
            ConfigManager.Settings.CircumventionMethod = item.Content?.ToString() ?? "Direct Connection";
        }

        ConfigManager.Save();
        
        // Tell Twitch IRC Client to switch channel
        if (App.LocalServer != null)
        {
            var twitchClient = App.LocalServer.App.Services.GetService(typeof(Server.TwitchIrcClient)) as Server.TwitchIrcClient;
            if (twitchClient != null)
            {
                twitchClient.SetChannel(ConfigManager.Settings.TwitchChannel);
            }
        }
    }

    private async void CopyUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(ObsUrlTextBox.Text ?? string.Empty);
        }
    }
}
