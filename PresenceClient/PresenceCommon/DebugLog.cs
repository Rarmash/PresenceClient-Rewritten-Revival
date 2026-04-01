using System;
using System.IO;

namespace PresenceCommon;

public static class DebugLog
{
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PresenceClient-Rewritten.log");

    public static void Log(string message)
    {
        lock (Sync)
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
    }

    public static void Log(Exception ex, string context)
    {
        Log($"{context}: {ex}");
    }

    public static string GetLogPath() => LogPath;
}
