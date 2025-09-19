using System;
using System.IO;
using System.Text;

namespace TTS.Shared.Utils
{
    public static class Logger
    {
        private static readonly string LogDir = GetLogDirectory();
        private static readonly string LogFile = Path.Combine(LogDir, "shared.log");
        private static readonly string GuiLogFile = Path.Combine(LogDir, "gui_messages.log");

        public static event Action<string>? GuiMessageReceived;

        private static string GetLogDirectory()
        {
            // Try to find the base directory (same logic as Settings)
            var baseDir = FindBaseDirectory();
            return Path.Combine(baseDir, "logs");
        }

        private static string FindBaseDirectory()
        {
            try
            {
                // First try the current working directory
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                
                // If that didn't work, try relative to the executable location
                var exeDir = new DirectoryInfo(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "");
                dir = exeDir;
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            
            // Fallback to current directory
            return Directory.GetCurrentDirectory();
        }

        static Logger()
        {
            Directory.CreateDirectory(LogDir);
        }

        public static void Write(string message, string level = "INFO")
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            Console.WriteLine(logMessage);
            
            try
            {
                File.AppendAllText(LogFile, logMessage + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void Gui(string message)
        {
            // Send message to GUI without timestamps
            GuiMessageReceived?.Invoke(message);
        }
    }
}
