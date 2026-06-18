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
    private bool _isUpdatingPreset = false;
    private bool _realExit = false;

    private ComboBoxItem ComboPreset1 = new ComboBoxItem();
    private ComboBoxItem ComboPreset2 = new ComboBoxItem();
    private ComboBoxItem ComboPreset3 = new ComboBoxItem();

    public MainWindow()
    {
        InitializeComponent();

        this.Closing += (s, e) => {
            if (ConfigManager.Settings.MinimizeToTray && !_realExit)
            {
                e.Cancel = true;
                this.Hide();
            }
        };

        var trayIcons = new Avalonia.Controls.TrayIcons();
        var trayIcon = new Avalonia.Controls.TrayIcon();
        trayIcon.ToolTipText = "TwiChatFHR";
        
        try {
            var iconUri = new Uri("avares://TwitchChatCore/Assets/app_main.png");
            var asset = Avalonia.Platform.AssetLoader.Open(iconUri);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(asset);
            trayIcon.Icon = new Avalonia.Controls.WindowIcon(bitmap);
        } catch {}
        
        trayIcon.Clicked += TrayOpen_Click;
        
        var trayMenu = new Avalonia.Controls.NativeMenu();
        
        var openItem = new Avalonia.Controls.NativeMenuItem();
        openItem.Bind(Avalonia.Controls.NativeMenuItem.HeaderProperty, this.GetResourceObservable("TrayOpen"));
        openItem.Click += TrayOpen_Click;
        
        var copyItem = new Avalonia.Controls.NativeMenuItem();
        copyItem.Bind(Avalonia.Controls.NativeMenuItem.HeaderProperty, this.GetResourceObservable("TrayCopyLink"));
        copyItem.Click += TrayCopyLink_Click;
        
        var exitItem = new Avalonia.Controls.NativeMenuItem();
        exitItem.Bind(Avalonia.Controls.NativeMenuItem.HeaderProperty, this.GetResourceObservable("TrayExit"));
        exitItem.Click += TrayExit_Click;

        trayMenu.Add(openItem);
        trayMenu.Add(copyItem);
        trayMenu.Add(new Avalonia.Controls.NativeMenuItemSeparator());
        trayMenu.Add(exitItem);
        
        trayIcon.Menu = trayMenu;
        trayIcons.Add(trayIcon);
        Avalonia.Controls.TrayIcon.SetIcons(Avalonia.Application.Current!, trayIcons);

        UpdateUIFromConfig();

        // Load Preset Names
        Preset1NameBox.Text = ConfigManager.Settings.CustomPresets.Count > 0 ? ConfigManager.Settings.CustomPresets[0].Name : "";
        Preset2NameBox.Text = ConfigManager.Settings.CustomPresets.Count > 1 ? ConfigManager.Settings.CustomPresets[1].Name : "";
        Preset3NameBox.Text = ConfigManager.Settings.CustomPresets.Count > 2 ? ConfigManager.Settings.CustomPresets[2].Name : "";

        this.Loaded += async (s, e) => {
            this.Topmost = true;
            this.Topmost = false;
            this.Activate();

            while (App.LocalServer == null || string.IsNullOrEmpty(App.LocalServer.BaseUrl))
            {
                await Task.Delay(50);
            }
            ObsUrlTextBox.Text = App.LocalServer.BaseUrl + "/index.html";
        };

        EmoteManager.OnEmoteDownloadProgress += (channel, processed, successful, total, speed) =>
        {
            // Throttle UI updates to avoid freezing UI with hundreds of events
            if (processed < total && total > 20 && processed % (total / 20) != 0) return;

            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                EmoteProgressPanel.IsVisible = true;
                EmoteErrorText.IsVisible = false;
                RetryEmotesButton.IsVisible = false;
                
                if (total == 0 || processed >= total)
                {
                    if (total > 0)
                    {
                        string providerName = "7TV";
                        string pureChannelName = channel;
                        if (channel.EndsWith("/7tv")) {
                            pureChannelName = channel.Substring(0, channel.Length - 4);
                        } else if (channel.EndsWith("/bttv")) {
                            providerName = "BTTV";
                            pureChannelName = channel.Substring(0, channel.Length - 5);
                        } else if (channel.EndsWith("/ffz")) {
                            providerName = "FFZ";
                            pureChannelName = channel.Substring(0, channel.Length - 4);
                        }
                        
                        EmoteProgressBar.Maximum = total;
                        EmoteProgressBar.Value = successful;
                        int failed = processed - successful;
                        
                        string normTpl = Application.Current!.FindResource("EmoteProgressNormal") as string ?? "7TV Emotes for {0} ({1}/{2})";
                        string errTpl = Application.Current!.FindResource("EmoteProgressWithErrors") as string ?? "7TV Emotes for {0} ({1}/{2}) [Errors: {3}]";
                        normTpl = normTpl.Replace("7TV", providerName);
                        errTpl = errTpl.Replace("7TV", providerName);

                        if (failed > 0)
                        {
                            EmoteProgressText.Text = string.Format(errTpl, pureChannelName, successful, total, failed);
                            EmoteProgressText.Foreground = Avalonia.Media.Brushes.Orange;
                        }
                        else
                        {
                            EmoteProgressText.Text = string.Format(normTpl, pureChannelName, successful, total);
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

        if (_isInitialized)
        {
            bool wasUpdating = _isUpdatingPreset;
            _isUpdatingPreset = true;

            int themeIdx = ThemeComboBox.SelectedIndex; ThemeComboBox.SelectedIndex = -1; ThemeComboBox.SelectedIndex = themeIdx;
            int borderIdx = BorderStyleComboBox.SelectedIndex; BorderStyleComboBox.SelectedIndex = -1; BorderStyleComboBox.SelectedIndex = borderIdx;
            int shapeIdx = ShapeComboBox.SelectedIndex; ShapeComboBox.SelectedIndex = -1; ShapeComboBox.SelectedIndex = shapeIdx;
            int layoutIdx = LayoutComboBox.SelectedIndex; LayoutComboBox.SelectedIndex = -1; LayoutComboBox.SelectedIndex = layoutIdx;
            int animIdx = AnimationComboBox.SelectedIndex; AnimationComboBox.SelectedIndex = -1; AnimationComboBox.SelectedIndex = animIdx;

            _isUpdatingPreset = wasUpdating;
        }
    }

    private void DesignSlider_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isInitialized || e.Property.Name != "Value") return;
        UpdateLabels();
        SetCustomPreset();
        CheckAndApplyPresetMatch();
        SaveDesignSettings();
    }

    private void DesignCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingPreset) return;
        SetCustomPreset();
        CheckAndApplyPresetMatch();
        SaveDesignSettings();
    }

    private void DesignComboBox_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingPreset) return;
        
        if (sender == ThemeComboBox)
        {
            TwitchChatCore.Core.Theme selectedTheme = (TwitchChatCore.Core.Theme)ThemeComboBox.SelectedIndex;

            if (selectedTheme == TwitchChatCore.Core.Theme.Custom1) { LoadPreset_Logic(0); return; }
            if (selectedTheme == TwitchChatCore.Core.Theme.Custom2) { LoadPreset_Logic(1); return; }
            if (selectedTheme == TwitchChatCore.Core.Theme.Custom3) { LoadPreset_Logic(2); return; }

            ApplyPreset(selectedTheme);
        }
        else
        {
            SetCustomPreset();
            CheckAndApplyPresetMatch();
        }
        
        SaveDesignSettings();
    }

    private void TextColorPicker_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingPreset) return;
        SetCustomPreset();
        CheckAndApplyPresetMatch();
        SaveDesignSettings();
    }

    private void SetCustomPreset()
    {
        if (!_isInitialized || _isUpdatingPreset) return;
        _isUpdatingPreset = true;
        ThemeComboBox.SelectedIndex = (int)TwitchChatCore.Core.Theme.Custom;
        _isUpdatingPreset = false;
    }

    private void CheckAndApplyPresetMatch()
    {
        if (!_isInitialized || _isUpdatingPreset) return;

        // Check if current settings match any preset exactly
        var theme = TwitchChatCore.Core.Theme.Custom;
        var msgBgColorHex = $"#{MessageBgColorPicker.Color.R:X2}{MessageBgColorPicker.Color.G:X2}{MessageBgColorPicker.Color.B:X2}";

        if (OpacitySlider.Value == 45 && BorderStyleComboBox.SelectedIndex == (int)MessageBorderStyle.Glass && ShapeComboBox.SelectedIndex == (int)MessageShape.Round && LayoutComboBox.SelectedIndex == (int)MessageLayout.Inline && TextOutlineCheck.IsChecked == true && HideBackgroundCheck.IsChecked == false && AnimationComboBox.SelectedIndex == (int)AnimationStyle.Pop && FontComboBox.SelectedIndex == (int)ChatFont.Outfit && msgBgColorHex == "#141923")
        {
            theme = TwitchChatCore.Core.Theme.Glass;
        }
        else if (OpacitySlider.Value == 80 && BorderStyleComboBox.SelectedIndex == (int)MessageBorderStyle.Neon && ShapeComboBox.SelectedIndex == (int)MessageShape.Square && LayoutComboBox.SelectedIndex == (int)MessageLayout.Inline && TextOutlineCheck.IsChecked == false && HideBackgroundCheck.IsChecked == false && AnimationComboBox.SelectedIndex == (int)AnimationStyle.Slide && FontComboBox.SelectedIndex == (int)ChatFont.CourierNew && msgBgColorHex == "#0A0514")
        {
            theme = TwitchChatCore.Core.Theme.Cyberpunk;
        }
        else if (OpacitySlider.Value == 0 && BorderStyleComboBox.SelectedIndex == (int)MessageBorderStyle.None && ShapeComboBox.SelectedIndex == (int)MessageShape.NoBorder && LayoutComboBox.SelectedIndex == (int)MessageLayout.Inline && TextOutlineCheck.IsChecked == true && HideBackgroundCheck.IsChecked == true && AnimationComboBox.SelectedIndex == (int)AnimationStyle.Fade && FontComboBox.SelectedIndex == (int)ChatFont.Outfit && msgBgColorHex == "#141923")
        {
            theme = TwitchChatCore.Core.Theme.Minimal;
        }
        else if (OpacitySlider.Value == 100 && BorderStyleComboBox.SelectedIndex == (int)MessageBorderStyle.Solid && ShapeComboBox.SelectedIndex == (int)MessageShape.Square && LayoutComboBox.SelectedIndex == (int)MessageLayout.Inline && TextOutlineCheck.IsChecked == false && HideBackgroundCheck.IsChecked == false && AnimationComboBox.SelectedIndex == (int)AnimationStyle.Pop && FontComboBox.SelectedIndex == (int)ChatFont.ComicSans && msgBgColorHex == "#000080")
        {
            theme = TwitchChatCore.Core.Theme.Retro;
        }

        if (theme != TwitchChatCore.Core.Theme.Custom && ThemeComboBox.SelectedIndex != (int)theme)
        {
            _isUpdatingPreset = true;
            ThemeComboBox.SelectedIndex = (int)theme;
            _isUpdatingPreset = false;
        }
    }

    private void ApplyPreset(TwitchChatCore.Core.Theme theme)
    {
        if (theme == TwitchChatCore.Core.Theme.Custom) return;
        
        _isUpdatingPreset = true;
        switch (theme)
        {
            case TwitchChatCore.Core.Theme.Glass:
                OpacitySlider.Value = 45;
                MessageBgColorPicker.Color = Color.Parse("#141923");
                BorderStyleComboBox.SelectedIndex = (int)MessageBorderStyle.Glass;
                ShapeComboBox.SelectedIndex = (int)MessageShape.Round;
                LayoutComboBox.SelectedIndex = (int)MessageLayout.Inline;
                TextOutlineCheck.IsChecked = true;
                HideBackgroundCheck.IsChecked = false;
                AnimationComboBox.SelectedIndex = (int)AnimationStyle.Pop;
                FontComboBox.SelectedIndex = (int)ChatFont.Outfit;
                break;
            case TwitchChatCore.Core.Theme.Cyberpunk:
                OpacitySlider.Value = 80;
                MessageBgColorPicker.Color = Color.Parse("#0A0514");
                BorderStyleComboBox.SelectedIndex = (int)MessageBorderStyle.Neon;
                ShapeComboBox.SelectedIndex = (int)MessageShape.Square;
                LayoutComboBox.SelectedIndex = (int)MessageLayout.Inline;
                TextOutlineCheck.IsChecked = false;
                HideBackgroundCheck.IsChecked = false;
                AnimationComboBox.SelectedIndex = (int)AnimationStyle.Slide;
                FontComboBox.SelectedIndex = (int)ChatFont.CourierNew;
                break;
            case TwitchChatCore.Core.Theme.Minimal:
                OpacitySlider.Value = 0;
                MessageBgColorPicker.Color = Color.Parse("#141923");
                BorderStyleComboBox.SelectedIndex = (int)MessageBorderStyle.None;
                ShapeComboBox.SelectedIndex = (int)MessageShape.NoBorder;
                LayoutComboBox.SelectedIndex = (int)MessageLayout.Inline;
                TextOutlineCheck.IsChecked = true;
                HideBackgroundCheck.IsChecked = true;
                AnimationComboBox.SelectedIndex = (int)AnimationStyle.Fade;
                FontComboBox.SelectedIndex = (int)ChatFont.Outfit;
                break;
            case TwitchChatCore.Core.Theme.Retro:
                OpacitySlider.Value = 100;
                MessageBgColorPicker.Color = Color.Parse("#000080");
                BorderStyleComboBox.SelectedIndex = (int)MessageBorderStyle.Solid;
                ShapeComboBox.SelectedIndex = (int)MessageShape.Square;
                LayoutComboBox.SelectedIndex = (int)MessageLayout.Inline;
                TextOutlineCheck.IsChecked = false;
                HideBackgroundCheck.IsChecked = false;
                AnimationComboBox.SelectedIndex = (int)AnimationStyle.Pop;
                FontComboBox.SelectedIndex = (int)ChatFont.ComicSans;
                break;
        }
        _isUpdatingPreset = false;
        UpdateLabels();
    }

    private void SaveDesignSettings()
    {
        ConfigManager.Settings.ChatFontSize = (int)FontSizeSlider.Value;
        ConfigManager.Settings.MessageSpacing = SpacingSlider.Value;
        ConfigManager.Settings.GlassOpacity = OpacitySlider.Value / 100.0;
        
        bool emotesChanged = false;
        if (ConfigManager.Settings.ShowStreamerEmotes != (StreamerEmotesCheck.IsChecked ?? true)) emotesChanged = true;
        if (ConfigManager.Settings.ShowBTTVEmotes != (ShowBTTVEmotesCheck.IsChecked ?? true)) emotesChanged = true;
        if (ConfigManager.Settings.ShowFFZEmotes != (ShowFFZEmotesCheck.IsChecked ?? true)) emotesChanged = true;
        
        ConfigManager.Settings.ShowStreamerEmotes = StreamerEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.ShowGlobalEmotes = GlobalEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.ShowGlobal7TVEmotes = Global7TVEmotesCheck.IsChecked ?? false;
        ConfigManager.Settings.ShowBTTVEmotes = ShowBTTVEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.ShowFFZEmotes = ShowFFZEmotesCheck.IsChecked ?? true;
        ConfigManager.Settings.EnableChatEffects = EnableChatEffectsCheck.IsChecked ?? false;
        
        ConfigManager.Settings.HideBackground = HideBackgroundCheck.IsChecked ?? false;
        ConfigManager.Settings.HideBadges = HideBadgesCheck.IsChecked ?? false;
        ConfigManager.Settings.HideBotMessages = HideBotMessagesCheck.IsChecked ?? false;
        ConfigManager.Settings.HideModMessages = HideModMessagesCheck.IsChecked ?? false;
        ConfigManager.Settings.HideVipMessages = HideVipMessagesCheck.IsChecked ?? false;
        ConfigManager.Settings.EnableRoleColors = EnableRoleColorsCheck.IsChecked ?? true;
        ConfigManager.Settings.TextOutline = TextOutlineCheck.IsChecked ?? false;
        ConfigManager.Settings.EnableJokeScript = EnableJokeScriptCheck.IsChecked ?? false;
        ConfigManager.Settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked ?? false;
        
        ConfigManager.Settings.DesignTheme = (TwitchChatCore.Core.Theme)(ThemeComboBox.SelectedIndex >= 0 ? ThemeComboBox.SelectedIndex : 0);
        ConfigManager.Settings.BorderStyle = (MessageBorderStyle)(BorderStyleComboBox.SelectedIndex >= 0 ? BorderStyleComboBox.SelectedIndex : 0);
        ConfigManager.Settings.DesignShape = (MessageShape)(ShapeComboBox.SelectedIndex >= 0 ? ShapeComboBox.SelectedIndex : 0);
        ConfigManager.Settings.DesignLayout = (MessageLayout)(LayoutComboBox.SelectedIndex >= 0 ? LayoutComboBox.SelectedIndex : 0);
        ConfigManager.Settings.AnimationType = (AnimationStyle)(AnimationComboBox.SelectedIndex >= 0 ? AnimationComboBox.SelectedIndex : 0);
        ConfigManager.Settings.Font = (ChatFont)(FontComboBox.SelectedIndex >= 0 ? FontComboBox.SelectedIndex : 0);
        
        ConfigManager.Settings.EnableMessageGrouping = EnableGroupingCheck.IsChecked ?? true;
        ConfigManager.Settings.HighlightMentions = HighlightMentionsCheck.IsChecked ?? false;
        ConfigManager.Settings.HighlightFirstMessage = HighlightFirstMessageCheck.IsChecked ?? true;
        
        // Save Hex color, ignoring alpha channel for CSS (e.g., #RRGGBB)
        var cBg = MessageBgColorPicker.Color;
        ConfigManager.Settings.MessageBgColor = $"#{cBg.R:X2}{cBg.G:X2}{cBg.B:X2}";
        var cGlobalBgColor = GlobalBgColorPicker.Color;
        ConfigManager.Settings.GlobalBgColor = $"#{cGlobalBgColor.A:X2}{cGlobalBgColor.R:X2}{cGlobalBgColor.G:X2}{cGlobalBgColor.B:X2}";
        var cText = TextColorPicker.Color;
        ConfigManager.Settings.CustomTextColor = $"#{cText.R:X2}{cText.G:X2}{cText.B:X2}";
        var cBrd = BroadcasterColorPicker.Color;
        ConfigManager.Settings.ColorBroadcaster = $"#{cBrd.R:X2}{cBrd.G:X2}{cBrd.B:X2}";
        var cMod = ModColorPicker.Color;
        ConfigManager.Settings.ColorMod = $"#{cMod.R:X2}{cMod.G:X2}{cMod.B:X2}";
        var cVip = VipColorPicker.Color;
        ConfigManager.Settings.ColorVip = $"#{cVip.R:X2}{cVip.G:X2}{cVip.B:X2}";
        
        _ = Task.Run(() => ConfigManager.Save());
        BroadcastDesignUpdate();
        
        if (emotesChanged && App.LocalServer?.App != null)
        {
            var ircClient = App.LocalServer.App.Services.GetService(typeof(TwitchChatCore.Server.TwitchIrcClient)) as TwitchChatCore.Server.TwitchIrcClient;
            ircClient?.ReloadEmotes();
        }
    }

    private void BroadcastDesignUpdate()
    {
        if (App.LocalServer?.App != null)
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
                    ""HideBotMessages"": {ConfigManager.Settings.HideBotMessages.ToString().ToLower()},
                    ""HideModMessages"": {ConfigManager.Settings.HideModMessages.ToString().ToLower()},
                    ""HideVipMessages"": {ConfigManager.Settings.HideVipMessages.ToString().ToLower()},
                    ""EnableRoleColors"": {ConfigManager.Settings.EnableRoleColors.ToString().ToLower()},
                    ""TextOutline"": {ConfigManager.Settings.TextOutline.ToString().ToLower()},
                    ""TextColor"": ""{ConfigManager.Settings.CustomTextColor}"",
                    ""MessageBgColor"": ""{ConfigManager.Settings.MessageBgColor}"",
                    ""GlobalBgColor"": ""{ConfigManager.Settings.GlobalBgColor}"",
                    ""ColorBroadcaster"": ""{ConfigManager.Settings.ColorBroadcaster}"",
                    ""ColorMod"": ""{ConfigManager.Settings.ColorMod}"",
                    ""ColorVip"": ""{ConfigManager.Settings.ColorVip}"",
                    ""AnimationType"": ""{ConfigManager.Settings.AnimationType.ToString().ToLower()}"",
                    ""EnableMessageGrouping"": {ConfigManager.Settings.EnableMessageGrouping.ToString().ToLower()},
                    ""HighlightMentions"": {ConfigManager.Settings.HighlightMentions.ToString().ToLower()},
                    ""HighlightFirstMessage"": {ConfigManager.Settings.HighlightFirstMessage.ToString().ToLower()},
                    ""DesignTheme"": ""{ConfigManager.Settings.DesignTheme.ToString().ToLower()}"",
                    ""BorderStyle"": ""{ConfigManager.Settings.BorderStyle.ToString().ToLower()}"",
                    ""DesignShape"": ""{ConfigManager.Settings.DesignShape.ToString().ToLower()}"",
                    ""DesignLayout"": ""{ConfigManager.Settings.DesignLayout.ToString().ToLower()}"",
                    ""Font"": ""{ConfigManager.Settings.Font.ToString().ToLower()}""
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

    private void EditBlacklist_Click(object? sender, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(TwitchChatCore.Core.ConfigManager.DataDir, "blacklist.txt");
        if (!System.IO.File.Exists(path))
        {
            System.IO.File.WriteAllText(path, "# Впишите сюда ники, которые нужно игнорировать (по одному на каждой строке)\n");
        }
        
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
            Console.WriteLine($"Could not open blacklist: {ex.Message}");
        }
    }

    private void EditBanPhrases_Click(object? sender, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(TwitchChatCore.Core.ConfigManager.DataDir, "ban_phrases.txt");
        if (!System.IO.File.Exists(path))
        {
            System.IO.File.WriteAllText(path, "# Впишите фразы для автобана (по одной на строке)\nя в нарезке\nя в телевизоре\nпередаю привет\n");
        }
        
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
            Console.WriteLine($"Could not open ban phrases: {ex.Message}");
        }
    }

    private void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var appDir = TwitchChatCore.Core.ConfigManager.AppDir;
            var updaterPath = System.IO.Path.Combine(appDir, "TwiChatUpdater.exe");
            
            if (System.IO.File.Exists(updaterPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
            else
            {
                // Optionally show a message that updater is missing
                Console.WriteLine("Updater not found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not start updater: {ex.Message}");
        }
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (TestModePanel != null)
        {
            if (UsernameTextBox.Text?.Trim().ToLower() == "test")
                TestModePanel.IsVisible = true;
            else
                TestModePanel.IsVisible = false;
        }
        SaveTechnicalSettings();
    }

    private void TestSpeedSlider_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (e.Property.Name == "Value")
        {
            if (TestSpeedValText != null && TestSpeedSlider != null)
            {
                TestSpeedValText.Text = TestSpeedSlider.Value.ToString();
            }

            if (App.LocalServer?.App != null && TestSpeedSlider != null)
            {
                var twitchClient = App.LocalServer.App.Services.GetService(typeof(Server.TwitchIrcClient)) as Server.TwitchIrcClient;
                if (twitchClient != null)
                {
                    twitchClient.SetTestSpeed((int)TestSpeedSlider.Value);
                }
            }
        }
    }

    private void LangComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        var lang = LangComboBox.SelectedIndex == 0 ? "ru" : "en";
        ConfigManager.Settings.Language = lang;
        
        _ = Task.Run(() => ConfigManager.Save());
        App.LoadLanguage(ConfigManager.Settings.Language);
        
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

        _ = Task.Run(() => ConfigManager.Save());
        TwitchChatCore.Core.NetworkManager.UpdateCustomWorker();
        
        // Tell Twitch IRC Client to switch channel
        if (App.LocalServer?.App != null)
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
        if (App.LocalServer?.App != null)
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

    private void UpdateUIFromConfig()
    {
        _isUpdatingPreset = true;
        // Load config to UI
        FontSizeSlider.Value = ConfigManager.Settings.ChatFontSize;
        SpacingSlider.Value = ConfigManager.Settings.MessageSpacing;
        OpacitySlider.Value = ConfigManager.Settings.GlassOpacity * 100;
        
        StreamerEmotesCheck.IsChecked = ConfigManager.Settings.ShowStreamerEmotes;
        GlobalEmotesCheck.IsChecked = ConfigManager.Settings.ShowGlobalEmotes;
        Global7TVEmotesCheck.IsChecked = ConfigManager.Settings.ShowGlobal7TVEmotes;
        ShowBTTVEmotesCheck.IsChecked = ConfigManager.Settings.ShowBTTVEmotes;
        ShowFFZEmotesCheck.IsChecked = ConfigManager.Settings.ShowFFZEmotes;
        EnableChatEffectsCheck.IsChecked = ConfigManager.Settings.EnableChatEffects;
        
        HideBackgroundCheck.IsChecked = ConfigManager.Settings.HideBackground;
        HideBadgesCheck.IsChecked = ConfigManager.Settings.HideBadges;
        HideBotMessagesCheck.IsChecked = ConfigManager.Settings.HideBotMessages;
        HideModMessagesCheck.IsChecked = ConfigManager.Settings.HideModMessages;
        HideVipMessagesCheck.IsChecked = ConfigManager.Settings.HideVipMessages;
        EnableRoleColorsCheck.IsChecked = ConfigManager.Settings.EnableRoleColors;
        TextOutlineCheck.IsChecked = ConfigManager.Settings.TextOutline;
        EnableJokeScriptCheck.IsChecked = ConfigManager.Settings.EnableJokeScript;
        
        ThemeComboBox.SelectedIndex = (int)ConfigManager.Settings.DesignTheme;
        BorderStyleComboBox.SelectedIndex = (int)ConfigManager.Settings.BorderStyle;
        ShapeComboBox.SelectedIndex = (int)ConfigManager.Settings.DesignShape;
        LayoutComboBox.SelectedIndex = (int)ConfigManager.Settings.DesignLayout;
        AnimationComboBox.SelectedIndex = (int)ConfigManager.Settings.AnimationType;
        FontComboBox.SelectedIndex = (int)ConfigManager.Settings.Font;
        
        EnableGroupingCheck.IsChecked = ConfigManager.Settings.EnableMessageGrouping;
        HighlightMentionsCheck.IsChecked = ConfigManager.Settings.HighlightMentions;
        HighlightFirstMessageCheck.IsChecked = ConfigManager.Settings.HighlightFirstMessage;

        if (Color.TryParse(ConfigManager.Settings.GlobalBgColor, out var cGlobalBg)) GlobalBgColorPicker.Color = cGlobalBg;
        if (Color.TryParse(ConfigManager.Settings.MessageBgColor, out var c0)) MessageBgColorPicker.Color = c0;
        if (Color.TryParse(ConfigManager.Settings.CustomTextColor, out var c1)) TextColorPicker.Color = c1;
        if (Color.TryParse(ConfigManager.Settings.ColorBroadcaster, out var c2)) BroadcasterColorPicker.Color = c2;
        if (Color.TryParse(ConfigManager.Settings.ColorMod, out var c3)) ModColorPicker.Color = c3;
        if (Color.TryParse(ConfigManager.Settings.ColorVip, out var c4)) VipColorPicker.Color = c4;

        if (string.IsNullOrWhiteSpace(ConfigManager.Settings.TwitchChannel)) {
            ConfigManager.Settings.TwitchChannel = "test";
        }
        UsernameTextBox.Text = ConfigManager.Settings.TwitchChannel;
        ServerPortTextBox.Text = ConfigManager.Settings.ServerPort.ToString();
        CustomWorkerTextBox.Text = ConfigManager.Settings.CustomWorkerUrl;
        
        UseTwitchProxySwitch.IsChecked = ConfigManager.Settings.UseTwitchProxy;
        ProxyListPanel.IsVisible = ConfigManager.Settings.UseTwitchProxy;
        ProxiesList.ItemsSource = ConfigManager.Settings.CloudProxies;

        if (ConfigManager.Settings.Language == "ru") LangComboBox.SelectedIndex = 0;
        else LangComboBox.SelectedIndex = 1;
        
        MinimizeToTrayCheck.IsChecked = ConfigManager.Settings.MinimizeToTray;

        if (ConfigManager.Settings.CustomPresets.Count > 0)
        {
            var p1 = ConfigManager.Settings.CustomPresets[0];
            ComboPreset1.Content = p1.Name;
            LoadPreset1Btn.Opacity = p1.IsSaved ? 1.0 : 0.3; ExportPreset1Btn.Opacity = p1.IsSaved ? 1.0 : 0.3;
            Preset1NameBox.Text = p1.IsSaved ? p1.Name : "";
            if (p1.IsSaved && !ThemeComboBox.Items.Contains(ComboPreset1)) ThemeComboBox.Items.Add(ComboPreset1);
            else if (!p1.IsSaved && ThemeComboBox.Items.Contains(ComboPreset1)) ThemeComboBox.Items.Remove(ComboPreset1);
        }
        if (ConfigManager.Settings.CustomPresets.Count > 1)
        {
            var p2 = ConfigManager.Settings.CustomPresets[1];
            ComboPreset2.Content = p2.Name;
            LoadPreset2Btn.Opacity = p2.IsSaved ? 1.0 : 0.3; ExportPreset2Btn.Opacity = p2.IsSaved ? 1.0 : 0.3;
            Preset2NameBox.Text = p2.IsSaved ? p2.Name : "";
            if (p2.IsSaved && !ThemeComboBox.Items.Contains(ComboPreset2)) ThemeComboBox.Items.Add(ComboPreset2);
            else if (!p2.IsSaved && ThemeComboBox.Items.Contains(ComboPreset2)) ThemeComboBox.Items.Remove(ComboPreset2);
        }
        if (ConfigManager.Settings.CustomPresets.Count > 2)
        {
            var p3 = ConfigManager.Settings.CustomPresets[2];
            ComboPreset3.Content = p3.Name;
            LoadPreset3Btn.Opacity = p3.IsSaved ? 1.0 : 0.3; ExportPreset3Btn.Opacity = p3.IsSaved ? 1.0 : 0.3;
            Preset3NameBox.Text = p3.IsSaved ? p3.Name : "";
            if (p3.IsSaved && !ThemeComboBox.Items.Contains(ComboPreset3)) ThemeComboBox.Items.Add(ComboPreset3);
            else if (!p3.IsSaved && ThemeComboBox.Items.Contains(ComboPreset3)) ThemeComboBox.Items.Remove(ComboPreset3);
        }

        _isUpdatingPreset = false;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
        {
            if (index < 0 || index >= ConfigManager.Settings.CustomPresets.Count) return;
            
            var preset = ConfigManager.Settings.CustomPresets[index];
            string inputName = index switch {
                0 => Preset1NameBox.Text ?? "",
                1 => Preset2NameBox.Text ?? "",
                _ => Preset3NameBox.Text ?? ""
            };
            
            if (string.IsNullOrWhiteSpace(inputName))
            {
                preset.IsSaved = false;
                preset.Name = "";
                ConfigManager.Save();
                UpdateUIFromConfig();
                
                ExportStatusText.IsVisible = true;
                ExportStatusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                string clearedText = Application.Current!.FindResource("PresetClearedText") as string ?? "✓ Слот очищен";
                ExportStatusText.Text = clearedText;
                Task.Delay(3000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                    if (ExportStatusText.Text == clearedText) ExportStatusText.IsVisible = false;
                }));
                return;
            }
            
            preset.Name = inputName;
            preset.IsSaved = true;
            
            preset.Font = ConfigManager.Settings.Font;
            preset.ChatFontSize = ConfigManager.Settings.ChatFontSize;
            preset.GlassOpacity = ConfigManager.Settings.GlassOpacity;
            preset.MessageSpacing = ConfigManager.Settings.MessageSpacing;
            
            preset.HideBackground = ConfigManager.Settings.HideBackground;
            preset.HideBadges = ConfigManager.Settings.HideBadges;
            preset.TextOutline = ConfigManager.Settings.TextOutline;
            preset.EnableRoleColors = ConfigManager.Settings.EnableRoleColors;
            
            preset.AnimationType = ConfigManager.Settings.AnimationType;
            preset.EnableMessageGrouping = ConfigManager.Settings.EnableMessageGrouping;
            preset.DesignShape = ConfigManager.Settings.DesignShape;
            preset.BorderStyle = ConfigManager.Settings.BorderStyle;
            preset.DesignLayout = ConfigManager.Settings.DesignLayout;
            
            preset.MessageBgColor = ConfigManager.Settings.MessageBgColor;
            preset.GlobalBgColor = ConfigManager.Settings.GlobalBgColor;
            preset.CustomTextColor = ConfigManager.Settings.CustomTextColor;
            preset.ColorBroadcaster = ConfigManager.Settings.ColorBroadcaster;
            preset.ColorMod = ConfigManager.Settings.ColorMod;
            preset.ColorVip = ConfigManager.Settings.ColorVip;
            
            preset.IsSaved = true;
            
            ConfigManager.Save();
            UpdateUIFromConfig();
            
            ExportStatusText.IsVisible = true;
            ExportStatusText.Text = "✓ Пресет сохранен";
            Task.Delay(3000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                if (ExportStatusText.Text == "✓ Пресет сохранен") ExportStatusText.IsVisible = false;
            }));
        }
    }

    private void LoadPreset_Logic(int index)
    {
        if (index < 0 || index >= ConfigManager.Settings.CustomPresets.Count) return;
        var preset = ConfigManager.Settings.CustomPresets[index];
        if (!preset.IsSaved) return;
        
        ConfigManager.Settings.Font = preset.Font;
        ConfigManager.Settings.ChatFontSize = preset.ChatFontSize;
        ConfigManager.Settings.GlassOpacity = preset.GlassOpacity;
        ConfigManager.Settings.MessageSpacing = preset.MessageSpacing;
        
        ConfigManager.Settings.HideBackground = preset.HideBackground;
        ConfigManager.Settings.HideBadges = preset.HideBadges;
        ConfigManager.Settings.TextOutline = preset.TextOutline;
        ConfigManager.Settings.EnableRoleColors = preset.EnableRoleColors;
        
        ConfigManager.Settings.AnimationType = preset.AnimationType;
        ConfigManager.Settings.EnableMessageGrouping = preset.EnableMessageGrouping;
        ConfigManager.Settings.DesignShape = preset.DesignShape;
        ConfigManager.Settings.BorderStyle = preset.BorderStyle;
        ConfigManager.Settings.DesignLayout = preset.DesignLayout;
        
        ConfigManager.Settings.MessageBgColor = preset.MessageBgColor;
        ConfigManager.Settings.GlobalBgColor = preset.GlobalBgColor;
        ConfigManager.Settings.CustomTextColor = preset.CustomTextColor;
        ConfigManager.Settings.ColorBroadcaster = preset.ColorBroadcaster;
        ConfigManager.Settings.ColorMod = preset.ColorMod;
        ConfigManager.Settings.ColorVip = preset.ColorVip;
        
        ConfigManager.Settings.DesignTheme = (TwitchChatCore.Core.Theme)(5 + index); // Custom1, Custom2, Custom3
        
        UpdateUIFromConfig();
        BroadcastDesignUpdate();
        ConfigManager.Save();
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
        {
            if (index < 0 || index >= ConfigManager.Settings.CustomPresets.Count) return;
            if (!ConfigManager.Settings.CustomPresets[index].IsSaved) return;
            
            LoadPreset_Logic(index);
            
            ExportStatusText.IsVisible = true;
            ExportStatusText.Text = "✓ Пресет загружен";
            Task.Delay(3000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                if (ExportStatusText.Text == "✓ Пресет загружен") ExportStatusText.IsVisible = false;
            }));
        }
    }

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
        {
            if (index < 0 || index >= ConfigManager.Settings.CustomPresets.Count) return;
            var preset = ConfigManager.Settings.CustomPresets[index];
            if (!preset.IsSaved) return;
            
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(preset, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string validFilename = string.Join("_", preset.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(validFilename)) validFilename = $"Preset_{index}";
                
                string docsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                string exportFolder = System.IO.Path.Combine(docsPath, "TwiChatFHR_Presets");
                if (!System.IO.Directory.Exists(exportFolder)) System.IO.Directory.CreateDirectory(exportFolder);
                
                string filePath = System.IO.Path.Combine(exportFolder, validFilename + ".json");
                System.IO.File.WriteAllText(filePath, json);
                
                ExportStatusText.IsVisible = true;
                ExportStatusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                string prefix = Application.Current!.FindResource("PresetExportedText") as string ?? "Пресет сохранён в: ";
                ExportStatusText.Text = prefix + filePath;
            }
            catch (Exception ex)
            {
                ExportStatusText.IsVisible = true;
                ExportStatusText.Foreground = Avalonia.Media.Brushes.Red;
                ExportStatusText.Text = "Ошибка экспорта: " + ex.Message;
                Task.Delay(4000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                    ExportStatusText.IsVisible = false;
                    ExportStatusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                }));
            }
        }
    }
    private void TrayOpen_Click(object? sender, EventArgs e)
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    private void TrayCopyLink_Click(object? sender, EventArgs e)
    {
        CopyUrl_Click(sender, new RoutedEventArgs());
    }

    private void TrayExit_Click(object? sender, EventArgs e)
    {
        _realExit = true;
        this.Close();
    }

    // Twitch Proxy Settings Handlers
    private void ProxySettings_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingPreset) return;
        
        ConfigManager.Settings.UseTwitchProxy = UseTwitchProxySwitch.IsChecked ?? false;
        ProxyListPanel.IsVisible = ConfigManager.Settings.UseTwitchProxy;
        ConfigManager.Save();
    }

    private void ProxyItem_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingPreset) return;
        ConfigManager.Save();
    }

    private void AddProxy_Click(object? sender, RoutedEventArgs e)
    {
        if (ConfigManager.Settings.CloudProxies == null)
            ConfigManager.Settings.CloudProxies = new System.Collections.ObjectModel.ObservableCollection<TwitchChatCore.Core.Models.CloudProxyServer>();

        var newProxy = new TwitchChatCore.Core.Models.CloudProxyServer 
        { 
            Name = $"Proxy {ConfigManager.Settings.CloudProxies.Count + 1}"
        };
        ConfigManager.Settings.CloudProxies.Add(newProxy);
        ConfigManager.Save();
    }

    private void RemoveProxy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is TwitchChatCore.Core.Models.CloudProxyServer proxy)
        {
            if (ConfigManager.Settings.CloudProxies != null)
            {
                ConfigManager.Settings.CloudProxies.Remove(proxy);
                ConfigManager.Save();
            }
        }
    }

    private void OpenProxyGuide_Click(object? sender, RoutedEventArgs e)
    {
        var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Hugging Face Proxy Guide</title>
    <style>
        body { font-family: sans-serif; background: #0F172A; color: #E2E8F0; padding: 40px; max-width: 800px; margin: 0 auto; line-height: 1.6; }
        h1, h2, h3 { color: #FFFFFF; }
        code { background: #1E293B; padding: 4px 8px; border-radius: 4px; font-family: monospace; color: #38BDF8; }
        pre { background: #1E293B; padding: 16px; border-radius: 8px; overflow-x: auto; border: 1px solid #334155; margin: 0; }
        pre code { background: transparent; padding: 0; color: #E2E8F0; }
        .step { background: #1E293B; border-radius: 8px; padding: 20px; margin-bottom: 20px; border: 1px solid #334155; }
        .highlight { color: #10B981; font-weight: bold; }
        .copy-wrapper { position: relative; margin: 10px 0; }
        .copy-btn { position: absolute; top: 12px; right: 12px; background: #334155; color: #E2E8F0; border: 1px solid #475569; padding: 6px 12px; border-radius: 6px; cursor: pointer; font-weight: 500; font-size: 12px; display: flex; align-items: center; gap: 6px; transition: all 0.2s; z-index: 10; }
        .copy-btn:hover { background: #475569; border-color: #64748B; color: #FFFFFF; }
        .copy-btn svg { width: 14px; height: 14px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    </style>
</head>
<body>
    <h1>Установка Proxy через Hugging Face (Бесплатно, без карт)</h1>
    <p>Мы используем популярный сервис Hugging Face Spaces. Он не требует привязки банковских карт, полностью бесплатен и отлично работает в РФ!</p>
    
    <div class='step'>
        <h2>Шаг 1: Создание проекта</h2>
        <ol>
            <li>Зарегистрируйтесь на <a href='https://huggingface.co/join' style='color:#3B82F6;' target='_blank'>Hugging Face</a> (нужна только почта).</li>
            <li>Перейдите в <a href='https://huggingface.co/spaces' style='color:#3B82F6;' target='_blank'>Spaces</a> и нажмите кнопку <b>Create new Space</b> в правом верхнем углу.</li>
            <li>Заполните форму:
                <ul>
                    <li><b>Space name:</b> любое имя (например, <code>twichat-proxy</code>)</li>
                    <li><b>License:</b> <code>mit</code> (или оставьте пустым)</li>
                    <li><b>Select the Space SDK:</b> выберите <b class='highlight'>Docker</b>, а затем шаблон <b class='highlight'>Blank</b></li>
                    <li><b>Space Hardware:</b> Free</li>
                </ul>
            </li>
            <li>Прокрутите вниз и нажмите <b>Create Space</b>.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 2: Установка пароля (Токена)</h2>
        <ol>
            <li>Перейдите в настройки спейса (кнопка <b>Settings</b> сверху справа).</li>
            <li>Прокрутите вниз до раздела <b>Variables and secrets</b> и нажмите <b>New secret</b>.</li>
            <li>В поле Name введите ровно: <code>PROXY_TOKEN</code></li>
            <li>В поле Value придумайте любой надежный пароль (например, <code>my-secret-123</code>) и нажмите Save. Это будет ваш Токен.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 3: Запуск сервера</h2>
        <ol>
            <li>Перейдите на вкладку <b>Files</b> (сверху справа).</li>
            <li>Нажмите <b>Contribute</b> (сверху справа) -> <b>Create a new file</b>.</li>
            <li>Назовите файл ровно: <code>Dockerfile</code></li>
            <li>Вставьте этот код в редактор файла:</li>
        </ol>
<div class='copy-wrapper'>
    <button class='copy-btn' onclick='copyCode(this)' title='Скопировать код'>
        <svg viewBox='0 0 24 24'><rect x='9' y='9' width='13' height='13' rx='2' ry='2'></rect><path d='M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1'></path></svg>
        <span>Скопировать</span>
    </button>
    <pre style='padding-right: 120px;'><code id='deploy-script'>FROM node:18-alpine
WORKDIR /app
RUN wget https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/CloudProxy/server.js -O server.js
RUN wget https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/CloudProxy/package.json -O package.json
RUN npm install --production
EXPOSE 7860
ENV PORT=7860
CMD [""npm"", ""start""]</code></pre>
</div>
        <ol start='5'>
            <li>Прокрутите вниз и нажмите <b>Commit new file to main</b>.</li>
            <li>Спейс начнет собираться. Вы можете нажать <b>Logs</b> (сверху справа), чтобы следить за процессом. Подождите 1-2 минуты, пока статус не изменится с <span style='color:#FCD34D;'>Starting</span> на <b class='highlight'>Running</b>.</li>
        </ol>
    </div>

    <div class='step'>
        <h2>Шаг 4: Настройка в TwiChatFHR</h2>
        <ol>
            <li>В приложении нажмите '+ Добавить прокси'.</li>
            <li>Откройте в спейсе вкладку <b>Logs</b>: там будет написан готовый <b>URL Сервера</b>. Просто скопируйте его и вставьте в приложение!</li>
            <li>В поле <b>Токен</b> впишите пароль, который вы придумали на Шаге 2.</li>
            <li>Включите маршрутизацию. Готово!</li>
        </ol>
    </div>

    <script>
        function copyCode(btn) {
            const code = document.getElementById('deploy-script').innerText;
            navigator.clipboard.writeText(code).then(() => {
                const span = btn.querySelector('span');
                const originalText = span.innerText;
                span.innerText = 'Скопировано!';
                btn.style.background = '#10B981';
                btn.style.borderColor = '#059669';
                btn.style.color = '#FFFFFF';
                setTimeout(() => { 
                    span.innerText = originalText; 
                    btn.style.background = '';
                    btn.style.borderColor = '';
                    btn.style.color = '';
                }, 2000);
            });
        }
    </script>
</body>
</html>";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TwiChatFHR_ProxyGuide.html");
        System.IO.File.WriteAllText(path, html);
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
