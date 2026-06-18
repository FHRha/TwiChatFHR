using System.Security.Cryptography;
using System.Text;

namespace TwitchChatCore.Server;

/// <summary>
/// Simple password hashing and verification using SHA-256 + salt.
/// Suitable for a self-hosted single-user admin panel.
/// </summary>
public static class AdminAuth
{
    public static string HashPassword(string password)
    {
        // Generate a random 16-byte salt
        var salt = RandomNumberGenerator.GetBytes(16);
        var saltB64 = Convert.ToBase64String(salt);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(saltB64 + password));
        var hashB64 = Convert.ToBase64String(hash);

        // Format: "salt$hash"
        return $"{saltB64}${hashB64}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$', 2);
        if (parts.Length != 2) return false;

        var saltB64 = parts[0];
        var storedHash = parts[1];

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(saltB64 + password));
        var computedHash = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    public static bool IsConfigured(string? hash) =>
        !string.IsNullOrWhiteSpace(hash);
}
