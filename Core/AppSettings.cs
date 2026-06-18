using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TwitchChatCore.Core.Models;

namespace TwitchChatCore.Core;

public enum SevenTVMode
{
    None,
    ChannelOnly,
    GlobalOnly,
    ChannelAndGlobal
}

public enum AnimationStyle { Pop, Slide, Fade }
public enum Theme { Glass, Cyberpunk, Minimal, Retro, Custom, Custom1, Custom2, Custom3 }
public enum MessageShape { Square, Round, Tail, NoBorder }
public enum MessageBorderStyle { Glass, Neon, Solid, None }
public enum MessageLayout { Inline, NickAbove, Columns }
public enum ChatFont { Outfit, Roboto, CourierNew, ComicSans, Impact }

public class CustomPreset
{
    public bool IsSaved { get; set; } = false;
    public string Name { get; set; } = "";
    public ChatFont Font { get; set; } = ChatFont.Outfit;
    public int ChatFontSize { get; set; } = 14;
    public double GlassOpacity { get; set; } = 0.45;
    public double MessageSpacing { get; set; } = 4;
    public bool HideBackground { get; set; } = false;
    public bool HideBadges { get; set; } = false;
    public bool TextOutline { get; set; } = true;
    public bool EnableRoleColors { get; set; } = true;
    public AnimationStyle AnimationType { get; set; } = AnimationStyle.Pop;
    public bool EnableMessageGrouping { get; set; } = true;
    public MessageShape DesignShape { get; set; } = MessageShape.Round;
    public MessageBorderStyle BorderStyle { get; set; } = MessageBorderStyle.Glass;
    public MessageLayout DesignLayout { get; set; } = MessageLayout.Inline;

    public string MessageBgColor { get; set; } = "#141923";
    public string GlobalBgColor { get; set; } = "#00000000";
    public string CustomTextColor { get; set; } = "#FFFFFF";
    public string ColorBroadcaster { get; set; } = "#F59E0B";
    public string ColorMod { get; set; } = "#10B981";
    public string ColorVip { get; set; } = "#EC4899";
}

public partial class AppSettings
{
    public List<CustomPreset> CustomPresets { get; set; } = new List<CustomPreset>
    {
        new CustomPreset { Name = "" },
        new CustomPreset { Name = "" },
        new CustomPreset { Name = "" }
    };

    public string TwitchChannel { get; set; } = "test";
    public int ServerPort { get; set; } = 0; // 0 means auto
    public string CircumventionMethod { get; set; } = "Direct Connection";
    public string ProxyAddress { get; set; } = "127.0.0.1";
    public int ProxyPort { get; set; } = 10809;
    public string SingboxLink { get; set; } = "";
    public bool EnableJokeScript { get; set; } = false;
    public SevenTVMode SevenTVEmotesMode { get; set; } = SevenTVMode.ChannelAndGlobal;
    public string GithubBadgesUrl { get; set; } = "https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/Server/Resources/global_badges.json";
    public string CustomWorkerUrl { get; set; } = "";
    
    // Twitch Proxy Settings
    public bool UseTwitchProxy { get; set; } = false;
    public bool UseStrictTwitchProxy { get; set; } = false;
    public ObservableCollection<CloudProxyServer> CloudProxies { get; set; } = new ObservableCollection<CloudProxyServer>();
    public DateTime LastQuotaResetDate { get; set; } = DateTime.UtcNow;

    // Emote Proxy Settings
    public bool UseCustomEmoteProxy { get; set; } = false;
    public bool UseStrictEmoteProxy { get; set; } = false;
    public bool UseTwitchProxyForEmotes { get; set; } = false;
    
    // UI Design Settings
    public ChatFont Font { get; set; } = ChatFont.Outfit;
    public int ChatFontSize { get; set; } = 14;
    public double GlassOpacity { get; set; } = 0.45;
    public double MessageSpacing { get; set; } = 4;
    
    // Feature Flags
    public bool EnableMessageGrouping { get; set; } = true;
    public bool HighlightMentions { get; set; } = false;
    public bool HighlightFirstMessage { get; set; } = true;
    
    // Emotes
    public bool ShowStreamerEmotes { get; set; } = true;
    public bool ShowGlobalEmotes { get; set; } = true;
    public bool ShowGlobal7TVEmotes { get; set; } = false;
    public bool ShowBTTVEmotes { get; set; } = false;
    public bool ShowFFZEmotes { get; set; } = false;
    
    // Chat Commands
    public bool EnableChatEffects { get; set; } = false;
    
    // Extra
    public bool HideBackground { get; set; } = false;
    public bool HideBadges { get; set; } = false;
    public bool HideBotMessages { get; set; } = false;
    public bool HideModMessages { get; set; } = false;
    public bool HideVipMessages { get; set; } = false;
    public bool TextOutline { get; set; } = true;
    public bool EnableRoleColors { get; set; } = true;
    
    // Premium Design Features
    public AnimationStyle AnimationType { get; set; } = AnimationStyle.Pop;
    public Theme DesignTheme { get; set; } = Theme.Glass;
    public MessageShape DesignShape { get; set; } = MessageShape.Round;
    public MessageBorderStyle BorderStyle { get; set; } = MessageBorderStyle.Glass;
    public MessageLayout DesignLayout { get; set; } = MessageLayout.Inline;

    public string MessageBgColor { get; set; } = "#141923";
    public string GlobalBgColor { get; set; } = "#00000000";
    public string CustomTextColor { get; set; } = "#FFFFFF";
    public string ColorBroadcaster { get; set; } = "#F59E0B";
    public string ColorMod { get; set; } = "#10B981";
    public string ColorVip { get; set; } = "#EC4899";
    public string Language { get; set; } = "ru";
    public bool MinimizeToTray { get; set; } = false;
}
