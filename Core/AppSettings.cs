namespace TwitchChatCore.Core;

public enum SevenTVMode
{
    None,
    ChannelOnly,
    ChannelAndGlobal
}

public class AppSettings
{
    public string TwitchChannel { get; set; } = "stintik";
    public int ServerPort { get; set; } = 0; // 0 means auto
    public string CircumventionMethod { get; set; } = "Direct Connection";
    public SevenTVMode SevenTVEmotesMode { get; set; } = SevenTVMode.ChannelAndGlobal;
    public string GithubBadgesUrl { get; set; } = "https://raw.githubusercontent.com/USERNAME/REPO/main/Server/Resources/global_badges.json";
}
