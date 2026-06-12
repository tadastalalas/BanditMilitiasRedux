using System;
using System.IO;
using System.Reflection;
using System.Text;
using TaleWorlds.Library;

namespace BanditMilitiasRedux.Helpers
{
    internal static class BMRLog
    {
        private static readonly object _gate = new();
        private static string _path;
        private static bool _initialized;

        internal static bool EnabledChatLogging => Globals.Settings?.EnableChatLogging == true;
        internal static bool EnabledFileLogging => Globals.Settings?.EnableFileLogging == true;

        internal static void WriteToChat(string message)
        {
            if (!EnabledChatLogging)
                return;

            InformationManager.DisplayMessage(new InformationMessage($"{message}"));
        }

        internal static void WriteToFile(string tag, string message)
        {
            if (!EnabledFileLogging) return;
            try
            {
                EnsureInitialized();
                if (_path is null) return;

                var line = $"{DateTime.Now:HH:mm:ss.fff} [{tag}] {message}{Environment.NewLine}";
                lock (_gate)
                {
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // never let logging crash the game
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(asmDir)) return;

                // …\Modules\BanditMilitiasRedux\bin\Win64_Shipping_Client → up to module root
                var moduleRoot = Directory.GetParent(asmDir)?.Parent?.FullName ?? asmDir;
                var logsDir = Path.Combine(moduleRoot, "logs");
                Directory.CreateDirectory(logsDir);

                _path = Path.Combine(logsDir, $"BMR_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.AppendAllText(_path,
                    $"--- BMR log started {DateTime.Now:O} ---{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
                _path = null;
            }
        }
    }
}