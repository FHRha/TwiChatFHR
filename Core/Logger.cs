using System;
using System.IO;

namespace TwitchChatCore.Core;

public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TwiChat_Logs.txt");
    private static readonly object _lock = new object();

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogFilePath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
            }
        }
        catch { }
    }

    public static void Clear()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogFilePath))
                    File.Delete(LogFilePath);
            }
        }
        catch { }
    }
}
