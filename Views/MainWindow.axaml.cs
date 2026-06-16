using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TwitchChatCore.Core;
using TwitchChatCore.Server;

namespace TwitchChatCore.Views;

public partial class MainWindow : Window
{
    private bool _isInitialized = false;

    public MainWindow()
    {
        InitializeComponent();

        // Load config to UI
        FontSizeSlider.Value = ConfigManager.Settings.ChatFontSize;
        SpacingSlider.Value = ConfigManager.Settings.MessageSpacing;
        OpacitySlider.Value = ConfigManager.Settings.GlassOpacity * 100;
        
        StreamerEmotesCheck.IsChecked = ConfigManager.Settings.ShowStreamerEmotes;
        GlobalEmotesCheck.IsChecked = ConfigManager.Settings.ShowGlobalEmotes;
        Global7TVEmotesCheck.IsChecked = ConfigManager.Settings.ShowGlobal7TVEmotes;
        HideBackgroundCheck.IsChecked = ConfigManager.Settings.HideBackground;
        HideBadgesCheck.IsChecked = ConfigManager.Settings.HideBadges;
        EnableRoleColorsCheck.IsChecked = ConfigManager.Settings.EnableRoleColors;
        TextOutlineCheck.IsChecked = ConfigManager.Settings.TextOutline;

        if (Color.TryParse(ConfigManager.Settings.CustomTextColor, out var c1)) TextColorPicker.Color = c1;
        if (Color.TryParse(ConfigManager.Settings.ColorBroadcaster, out var c2)) BroadcasterColorPicker.Color = c2;
        if (Color.TryParse(ConfigManager.Settings.ColorMod, out var c3)) ModColorPicker.Color = c3;
        if (Color.TryParse(ConfigManager.Settings.ColorVip, out var c4)) VipColorPicker.Color = c4;

        UsernameTextBox.Text = ConfigManager.Settings.TwitchChannel;
        ServerPortTextBox.Text = ConfigManager.Settings.ServerPort.ToString();
        CustomWorkerTextBox.Text = ConfigManager.Settings.CustomWorkerUrl;

        if (ConfigManager.Settings.Language == "ru") LangComboBox.SelectedIndex = 0;
        else LangComboBox.SelectedIndex = 1;

        this.Loaded += (s, e) => {
            if (App.LocalServer != null)
            {
                ObsUrlTextBox.Text = App.LocalServer.BaseUrl + "/index.html";
            }
        };

        EmoteManager.OnEmoteDownloadProgress += (channel, processed, successful, total, speed) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                EmoteProgressPanel.IsVisible = true;
                EmoteErrorText.IsVisible = false;
                RetryEmotesButton.IsVisible = false;
                
                if (total == 0 || processed >= total)
                {
                    if (total > 0)
                    {
                        EmoteProgressBar.Maximum = total;
                        EmoteProgressBar.Value = successful;
                        int failed = processed - successful;
                        if (failed > 0)
                        {
                            EmoteProgressText.Text = string.Format(Application.Current!.FindResource("EmoteProgressWithErrors") as string ?? "7TV Emotes for {0} ({1}/{2}) [Errors: {3}]", channel, successful, total, failed);
                            EmoteProgressText.Foreground = Avalonia.Media.Brushes.Orange;
                        }
                        else
                        {
                            EmoteProgressText.Text = string.Format(Application.Current!.FindResource("EmoteProgressNormal") as string ?? "7TV Emotes for {0} ({1}/{2})", channel, successful, total);
                            EmoteProgressText.Foreground = Avalonia.Media.Brushes.LightGreen;
                        }
                    }
                    else
                    {
                        EmoteProgressBar.Maximum = 100;
                        EmoteProgressBar.Value = 100;
                        EmoteProgressText.Text = string.Format(Application.Current!.FindResource("EmoteProgressCached") as string ?? "7TV Emotes for {0} (Cached)", channel);
                        EmoteProgressText.Foreground = Avalonia.Media.Brushes.LightGreen;
                    }

                    EmoteSpeedText.Text = Application.Current!.FindResource("EmoteStatusDone") as string ?? "Done";
                    await Task.Delay(2000);
                    EmoteSpeedText.Text = "";
                }
                else
                {
                    EmoteProgressText.Foreground = Avalonia.Media.Brushes.White;
                    EmoteProgressBar.Maximum = total;
                    EmoteProgressBar.Value = successful;
                    EmoteProgressText.Text = string.Format(Application.Current!.FindResource("EmoteProgressNormal") as string ?? "7TV Emotes for {0} ({1}/{2})", channel, successful, total);
                    EmoteSpeedText.Text = string.Format(Application.Current!.FindResource("EmoteSpeed") as string ?? "{0:F1} em/s", speed);
                }
            });
        };

        EmoteManager.OnEmoteDownloadError += (channel, error) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                EmoteProgressPanel.IsVisible = true;
                EmoteProgressText.Text = string.Format(Application.Current!.FindResource("EmoteProgressFailed") as string ?? "Failed: 7TV Emotes for {0}", channel);
                EmoteProgressText.Foreground = Avalonia.Media.Brushes.LightCoral;
                EmoteProgressBar.Foreground = Avalonia.Media.Brushes.Red;
                EmoteSpeedText.Text = Application.Current!.FindResource("EmoteStatusError") as string ?? "Error";
                EmoteErrorText.Text = error;
                EmoteErrorText.IsVisible = true;
                RetryEmotesButton.IsVisible = true;
            });
        };

        UpdateLabels();
        
        _isInitialized = true;
    }

    private void UpdateLabels()
    {
        FontSizeValText.Text = $"{FontSizeSlider.Value}px";
        SpacingValText.Text = $"{SpacingSlider.Value}px";
        OpacityValText.Text = $"{OpacitySlider.Value}%";
    }

    private void DesignSlider_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isInitialized || e.Property.Name != "Value") return;
        UpdateLabels();
        SaveDesignSettings();
    }

    private void DesignCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        SaveDesignSettings();
    }

    private void TextColorPicker_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (!_isInitialized) return;
        SaveDesignSettings();
    }

    private void SaveDesignSettings()
    {
        ConfigManager.Settings.ChatFontSize = (int)FontSizeSlider.Value;
        ConfigManager.Settings.MessageSpacing = SpacingSlider.Value;
        ConfigManager.Settings.GlassOpacity = OpacitySlider.Value / 100.0;
        
        ConfigManager.Settings.ShowStreamerEmotes = StreamerEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.ShowGlobalEmotes = GlobalEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.ShowGlobal7TVEmotes = Global7TVEmotesCheck.IsChecked ?? false;
        ConfigManager.Settings.HideBackground = HideBackgroundCheck.IsChecked ?? false;
        ConfigManager.Settings.HideBadges = HideBadgesCheck.IsChecked ?? false;
        ConfigManager.Settings.EnableRoleColors = EnableRoleColorsCheck.IsChecked ?? true;
        ConfigManager.Settings.TextOutline = TextOutlineCheck.IsChecked ?? false;
        
        // Save Hex color, ignoring alpha channel for CSS (e.g., #RRGGBB)
        var cText = TextColorPicker.Color;
        ConfigManager.Settings.CustomTextColor = $"#{cText.R:X2}{cText.G:X2}{cText.B:X2}";
        var cBrd = BroadcasterColorPicker.Color;
        ConfigManager.Settings.ColorBroadcaster = $"#{cBrd.R:X2}{cBrd.G:X2}{cBrd.B:X2}";
        var cMod = ModColorPicker.Color;
        ConfigManager.Settings.ColorMod = $"#{cMod.R:X2}{cMod.G:X2}{cMod.B:X2}";
        var cVip = VipColorPicker.Color;
        ConfigManager.Settings.ColorVip = $"#{cVip.R:X2}{cVip.G:X2}{cVip.B:X2}";
        
        ConfigManager.Save();
        BroadcastDesignUpdate();
    }

    private void BroadcastDesignUpdate()
    {
        if (App.LocalServer != null)
        {
            var chatHub = App.LocalServer.App.Services.GetService(typeof(ChatHub)) as ChatHub;
            if (chatHub != null)
            {
                string json = $@"{{
                    ""Type"": ""ConfigUpdate"",
                    ""FontSize"": {ConfigManager.Settings.ChatFontSize},
                    ""Spacing"": {ConfigManager.Settings.MessageSpacing},
                    ""Opacity"": {ConfigManager.Settings.GlassOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                    ""ShowStreamerEmotes"": {ConfigManager.Settings.ShowStreamerEmotes.ToString().ToLower()},
                    ""ShowGlobalEmotes"": {ConfigManager.Settings.ShowGlobalEmotes.ToString().ToLower()},
                    ""ShowGlobal7TVEmotes"": {ConfigManager.Settings.ShowGlobal7TVEmotes.ToString().ToLower()},
                    ""HideBackground"": {ConfigManager.Settings.HideBackground.ToString().ToLower()},
                    ""HideBadges"": {ConfigManager.Settings.HideBadges.ToString().ToLower()},
                    ""EnableRoleColors"": {ConfigManager.Settings.EnableRoleColors.ToString().ToLower()},
                    ""TextOutline"": {ConfigManager.Settings.TextOutline.ToString().ToLower()},
                    ""TextColor"": ""{ConfigManager.Settings.CustomTextColor}"",
                    ""ColorBroadcaster"": ""{ConfigManager.Settings.ColorBroadcaster}"",
                    ""ColorMod"": ""{ConfigManager.Settings.ColorMod}"",
                    ""ColorVip"": ""{ConfigManager.Settings.ColorVip}""
                }}";
                _ = chatHub.BroadcastMessageAsync(json);
            }
        }
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        PreviewPanel.IsVisible = false;
        OverlayBackground.IsVisible = true;
        SettingsModal.IsVisible = true;
    }

    private void CloseSettings_Click(object? sender, RoutedEventArgs e)
    {
        OverlayBackground.IsVisible = false;
        SettingsModal.IsVisible = false;
        PreviewPanel.IsVisible = true;
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SaveTechnicalSettings();
    }

    private void LangComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        var lang = LangComboBox.SelectedIndex == 0 ? "ru" : "en";
        ConfigManager.Settings.Language = lang;
        
        ConfigManager.Save();
        App.LoadLanguage(ConfigManager.Settings.Language);
        SaveTechnicalSettings();
        
        UpdateLabels();
    }

    private void SaveTechnicalSettings()
    {
        if (!_isInitialized) return;

        ConfigManager.Settings.TwitchChannel = UsernameTextBox.Text ?? "";
        ConfigManager.Settings.CustomWorkerUrl = CustomWorkerTextBox.Text ?? "";
        if (int.TryParse(ServerPortTextBox.Text, out int port))
        {
            ConfigManager.Settings.ServerPort = port;
        }

        ConfigManager.Save();
        TwitchChatCore.Core.NetworkManager.UpdateCustomWorker();
        
        // Tell Twitch IRC Client to switch channel
        if (App.LocalServer != null)
        {
            var twitchClient = App.LocalServer.App.Services.GetService(typeof(Server.TwitchIrcClient)) as Server.TwitchIrcClient;
            if (twitchClient != null)
            {
                twitchClient.SetChannel(ConfigManager.Settings.TwitchChannel);
                
                var chatHub = App.LocalServer.App.Services.GetService(typeof(Server.ChatHub)) as Server.ChatHub;
                if (chatHub != null)
                {
                    _ = chatHub.BroadcastMessageAsync("{\"Type\": \"ClearChat\"}");
                }
            }
        }
    }

    private void RetryEmotes_Click(object? sender, RoutedEventArgs e)
    {
        RetryEmotesButton.IsVisible = false;
        if (App.LocalServer != null)
        {
            var twitchClient = App.LocalServer.App.Services.GetService(typeof(Server.TwitchIrcClient)) as Server.TwitchIrcClient;
            if (twitchClient != null)
            {
                // Force a reload by re-setting the same channel
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

    private void OpenWorkerGuide_Click(object? sender, RoutedEventArgs e)
    {
        var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Google Apps Script Proxy Guide</title>
    <style>
        body { font-family: sans-serif; background: #0F172A; color: #E2E8F0; padding: 40px; max-width: 800px; margin: 0 auto; line-height: 1.6; }
        h1, h2, h3 { color: #FFFFFF; }
        code { background: #1E293B; padding: 4px 8px; border-radius: 4px; font-family: monospace; color: #38BDF8; }
        pre { background: #1E293B; padding: 16px; border-radius: 8px; overflow-x: auto; border: 1px solid #334155; }
        pre code { background: transparent; padding: 0; color: #E2E8F0; }
        .step { background: #1E293B; border-radius: 8px; padding: 20px; margin-bottom: 20px; border: 1px solid #334155; }
        .highlight { color: #10B981; font-weight: bold; }
    </style>
</head>
<body>
    <h1>Создание пуленепробиваемого 7TV Proxy (через Google)</h1>
    <p>Так как многие сторонние сервисы могут блокироваться вашим провайдером, мы предлагаем использовать сервера <b>Google</b>. Их в РФ не блокируют, иначе сломаются все Android-смартфоны и сервисы.</p>
    
    <div class='step'>
        <h2>Шаг 1: Создание скрипта</h2>
        <ol>
            <li>Убедитесь, что у вас есть Google аккаунт, и зайдите на <a href='https://script.google.com' style='color:#3B82F6;'>script.google.com</a>.</li>
            <li>Нажмите синюю кнопку <b>New project</b> (Новый проект) слева вверху.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 2: Вставка кода</h2>
        <ol>
            <li>В открывшемся редакторе удалите весь стандартный код <code>function myFunction() { ... }</code>.</li>
            <li>Вставьте следующий код целиком:</li>
        </ol>
<pre><code>function doGet(e) {
  var url = e.parameter.url;
  if (!url) return ContentService.createTextOutput('No URL provided');
  
  try {
    var response = UrlFetchApp.fetch(url, { muteHttpExceptions: true });
    var contentType = response.getHeaders()['Content-Type'] || response.getHeaders()['content-type'] || '';
    
    if (contentType.indexOf('application/json') !== -1 || contentType.indexOf('text/') !== -1) {
      return ContentService.createTextOutput(response.getContentText())
                           .setMimeType(ContentService.MimeType.JSON);
    } else {
      var base64 = Utilities.base64Encode(response.getBlob().getBytes());
      return ContentService.createTextOutput(base64)
                           .setMimeType(ContentService.MimeType.TEXT);
    }
  } catch (err) {
    return ContentService.createTextOutput(err.toString());
  }
}</code></pre>
        <ol start='3'>
            <li>Нажмите иконку <b>Сохранить</b> (Дискету) над кодом (или Ctrl+S). Проект можно назвать как угодно.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 3: Публикация и получение ссылки</h2>
        <ol>
            <li>В правом верхнем углу нажмите синюю кнопку <b>Deploy</b> (Начать развертывание) -> <b>New deployment</b> (Новое развертывание).</li>
            <li>Нажмите на шестеренку (Select type) рядом с 'Select type' слева и выберите <b>Web app</b> (Веб-приложение).</li>
            <li>В поле <b>Who has access</b> (У кого есть доступ) ОБЯЗАТЕЛЬНО выберите <b class='highlight'>Anyone</b> (Все).</li>
            <li>Нажмите <b>Deploy</b> (Начать развертывание). При первом запуске Google может попросить 'Review Permissions' (Предоставить доступ). Нажмите 'Review Permissions', выберите свой аккаунт, затем 'Advanced' (Дополнительные настройки) и 'Go to project (unsafe)' (Перейти на страницу проекта). И нажмите 'Allow'.</li>
            <li>Появится окно с вашей ссылкой <b>Web app URL</b> (она длинная, начинается на <code>https://script.google.com/macros/...</code>). Скопируйте её.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 4: Настройка в TwiChatFHR</h2>
        <ol>
            <li>Откройте настройки TwiChatFHR.</li>
            <li>В поле <b>Custom Worker URL</b> просто вставьте скопированную ссылку. <b>Ничего дописывать в конец не нужно!</b> Приложение само подставит нужные параметры.</li>
            <li>Пример: <code>https://script.google.com/macros/s/ВАШ_ИД/exec</code></li>
            <li>Готово! Приложение начнет скачивать эмоуты через сервера Google.</li>
        </ol>
    </div>
</body>
</html>";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TwiChatFHR_WorkerGuide.html");
        System.IO.File.WriteAllText(path, html);
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open guide: {ex.Message}");
        }
    }
}
