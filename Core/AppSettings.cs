namespace TwitchChatCore.Core;

public enum SevenTVMode
{
    None,
    ChannelOnly,
    GlobalOnly,
    ChannelAndGlobal
}

public class AppSettings
{
    public string TwitchChannel { get; set; } = "stintik";
    public int ServerPort { get; set; } = 0; // 0 means auto
    public string CircumventionMethod { get; set; } = "Direct Connection";
    public string ProxyAddress { get; set; } = "127.0.0.1";
    public int ProxyPort { get; set; } = 10809;
    public string SingboxLink { get; set; } = "";
    public SevenTVMode SevenTVEmotesMode { get; set; } = SevenTVMode.ChannelAndGlobal;
    public string GithubBadgesUrl { get; set; } = "https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/Server/Resources/global_badges.json";
    public string CustomWorkerUrl { get; set; } = "";
    
    // UI Design Settings
    public int ChatFontSize { get; set; } = 14;
    public double GlassOpacity { get; set; } = 0.45;
    public double MessageSpacing { get; set; } = 4;
    
    // Feature Flags
    public bool ShowStreamerEmotes { get; set; } = true;
    public bool ShowGlobalEmotes { get; set; } = true;
    public bool ShowGlobal7TVEmotes { get; set; } = false;
    public bool HideBackground { get; set; } = false;
    public bool HideBadges { get; set; } = false;
    public bool TextOutline { get; set; } = false;
    
    public string CustomTextColor { get; set; } = "#FFFFFF";
    public string Language { get; set; } = "ru";
}
