namespace TwitchChatCore.Core;

// Extends AppSettings with web-only fields (auth credentials).
// Kept separate so the original desktop project is not affected.
public partial class AppSettings
{
    /// <summary>Login for the admin web panel.</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Salt+SHA256 hash of the admin password. Empty = first-run setup needed.</summary>
    public string AdminPasswordHash { get; set; } = "";
}
