using System;
using System.IO;
using BanditMilitiasRedux.Core;

namespace BanditMilitiasRedux.Diagnostics;

// File logger: Modules\BanditMilitiasRedux\logs\BanditMilitiasRedux_<date>.log
// (ProgramData fallback when the module folder is read-only). Never writes to the screen.
internal static class BmrLog
{
    private const string FilePrefix = ModInfo.ModuleId + "_";
    private const string FileDateFormat = "yyyyMMdd";
    private const string FileExtension = ".log";
    private const string FallbackGameFolder = "Mount and Blade II Bannerlord";
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _disabled;

    // Hot paths: test this before building the message string.
    internal static bool IsDebugEnabled => ModSettings.LoggingEnabled;

    // Debug detail: written only when "Logging for debugging" is on.
    internal static void Debug(string message)
    {
        if (ModSettings.LoggingEnabled) Write("DEBUG", message);
    }

    // Problems: always written.
    internal static void Warn(string message) => Write("WARN", message);

    internal static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        if (_disabled) return;
        lock (Gate)
        {
            try
            {
                _path ??= ResolvePath();
                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                _disabled = true; // logging must never take the game down
            }
        }
    }

    private static string ResolvePath()
    {
        string fileName = FilePrefix + DateTime.Now.ToString(FileDateFormat) + FileExtension;
        string moduleLogs = Path.Combine(ModInfo.ModuleRoot, ModInfo.LogFolderName);
        try
        {
            Directory.CreateDirectory(moduleLogs);
            return Path.Combine(moduleLogs, fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FallbackGameFolder, ModInfo.LogFolderName);
            Directory.CreateDirectory(fallback);
            return Path.Combine(fallback, fileName);
        }
    }
}
