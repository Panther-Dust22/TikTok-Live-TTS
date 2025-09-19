using System;
using System.IO;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class Settings
    {
        public int MaxInFlightJobs { get; set; } = 4;

        public Options Options { get; private set; } = new();
        public FilterConfig Filter { get; private set; } = new();
        public Users Users { get; private set; } = new();
        public Voices Voices { get; private set; } = new();
        public AppConfig App { get; private set; } = new();

        private DateTime _lastLoad = DateTime.MinValue;
        private (DateTime, DateTime, DateTime, DateTime, DateTime) _stamps;

        private static string? _baseDir;

        public static Settings Load()
        {
            Logger.Write($"[SETTINGS] Settings.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            var s = new Settings();
            Logger.Write($"[SETTINGS] New Settings object created");
            
            // Establish base directory once (root containing 'data')
            Logger.Write($"[SETTINGS] Calling FindBaseDir() at {DateTime.Now:HH:mm:ss.fff}");
            _baseDir = FindBaseDir();
            Logger.Write($"[SETTINGS] FindBaseDir() returned: {_baseDir}");
            Console.WriteLine($"[CFG] BaseDir={_baseDir}");
            
            Logger.Write($"[SETTINGS] Calling ReloadAll() at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                s.ReloadAll();
                Logger.Write($"[SETTINGS] ReloadAll() completed at {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.Write($"[SETTINGS] ERROR in ReloadAll(): {ex.Message}", level: "ERROR");
                Logger.Write($"[SETTINGS] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
            
            Logger.Write($"[SETTINGS] Voices count: {s.Voices?.Voice_List_cheat_sheet?.Count}");
            Logger.Write($"[SETTINGS] Options map count: {s.Options?.D_voice_map?.Count}");
            Logger.Write($"[SETTINGS] Users priority count: {s.Users?.C_priority_voice?.Count}");
            Console.WriteLine($"[CFG] Loaded: voices={s.Voices?.Voice_List_cheat_sheet?.Count} options.map={s.Options?.D_voice_map?.Count} users.priority={s.Users?.C_priority_voice?.Count}");
            Logger.Write($"[SETTINGS] Settings.Load() completed successfully at {DateTime.Now:HH:mm:ss.fff}");
            return s;
        }

        public void ReloadIfChanged()
        {
            var stamps = GetStamps();
            if (stamps != _stamps)
            {
                ReloadAll();
            }
        }

        private void ReloadAll()
        {
            Logger.Write($"[RELOADALL] ReloadAll() called at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                Logger.Write($"[RELOADALL] Loading Options at {DateTime.Now:HH:mm:ss.fff}");
                Options = Options.Load();
                Logger.Write($"[RELOADALL] Options loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                Logger.Write($"[RELOADALL] Loading Filter at {DateTime.Now:HH:mm:ss.fff}");
                Filter = FilterConfig.Load();
                Logger.Write($"[RELOADALL] Filter loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                Logger.Write($"[RELOADALL] Loading Users at {DateTime.Now:HH:mm:ss.fff}");
                Users = Users.Load();
                Logger.Write($"[RELOADALL] Users loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                Logger.Write($"[RELOADALL] Loading Voices at {DateTime.Now:HH:mm:ss.fff}");
                Voices = Voices.Load();
                Logger.Write($"[RELOADALL] Voices loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                Logger.Write($"[RELOADALL] Loading App at {DateTime.Now:HH:mm:ss.fff}");
                App = AppConfig.Load();
                Logger.Write($"[RELOADALL] App loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                Logger.Write($"[RELOADALL] All configs loaded, getting stamps at {DateTime.Now:HH:mm:ss.fff}");
                _stamps = GetStamps();
                Logger.Write($"[RELOADALL] Stamps retrieved at {DateTime.Now:HH:mm:ss.fff}");
                
                _lastLoad = DateTime.UtcNow;
                Logger.Write($"[RELOADALL] ReloadAll() completed successfully at {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.Write($"[RELOADALL] ERROR in ReloadAll(): {ex.Message}", level: "ERROR");
                Logger.Write($"[RELOADALL] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
            Console.WriteLine($"[CFG] ReloadAll: Default={Options?.D_voice_map?.GetValueOrDefault("Default")} Subscriber={Options?.D_voice_map?.GetValueOrDefault("Subscriber")}");
        }

        public void ForceReloadFromDisk()
        {
            ReloadAll();
        }

        private static (DateTime, DateTime, DateTime, DateTime, DateTime) GetStamps()
        {
            return (
                Stamp(PathResolver("data", "options.json")),
                Stamp(PathResolver("data", "filter.json")),
                Stamp(PathResolver("data", "user_management.json")),
                Stamp(PathResolver("data", "voices.json")),
                Stamp(PathResolver("data", "config.json"))
            );
        }

        private static DateTime Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        public static string PathResolver(params string[] parts)
        {
            Logger.Write($"[PATHRESOLVER] PathResolver called with {parts.Length} parts at {DateTime.Now:HH:mm:ss.fff}");
            Logger.Write($"[PATHRESOLVER] Parts: {string.Join(", ", parts)}");
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            Logger.Write($"[PATHRESOLVER] Starting from directory: {dir.FullName}");
            for (int i = 0; i < 6 && dir != null; i++)
            {
                Logger.Write($"[PATHRESOLVER] Iteration {i}, checking directory: {dir.FullName}");
                var p = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
                Logger.Write($"[PATHRESOLVER] Checking path: {p}");
                if (File.Exists(p)) 
                {
                    Logger.Write($"[PATHRESOLVER] File found: {p}");
                    return p;
                }
                dir = dir.Parent;
            }
            var fallback = Path.Combine(parts);
            Logger.Write($"[PATHRESOLVER] No file found, returning fallback: {fallback}");
            return fallback;
        }

        public static string ResolveDataPath(string fileName)
        {
            var baseDir = _baseDir ?? FindBaseDir();
            var fullPath = Path.Combine(baseDir, "data", fileName);
            Logger.Write($"[PATH] ResolveDataPath: {fileName} -> {fullPath} (baseDir: {baseDir}, currentDir: {Directory.GetCurrentDirectory()})", level: "DEBUG");
            return fullPath;
        }

        private static string FindBaseDir()
        {
            try
            {
                // First try the current working directory
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                Logger.Write($"[PATH] FindBaseDir starting from: {dir.FullName}", level: "DEBUG");
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    Logger.Write($"[PATH] Checking: {dataDir} (exists: {Directory.Exists(dataDir)}, has options.json: {File.Exists(Path.Combine(dataDir, "options.json"))})", level: "DEBUG");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        Logger.Write($"[PATH] Found base dir: {dir.FullName}", level: "DEBUG");
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                
                // If that didn't work, try relative to the executable location
                var exeDir = new DirectoryInfo(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "");
                Logger.Write($"[PATH] Trying executable directory: {exeDir.FullName}", level: "DEBUG");
                dir = exeDir;
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    Logger.Write($"[PATH] Checking exe path: {dataDir} (exists: {Directory.Exists(dataDir)}, has options.json: {File.Exists(Path.Combine(dataDir, "options.json"))})", level: "DEBUG");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        Logger.Write($"[PATH] Found base dir from exe: {dir.FullName}", level: "DEBUG");
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            catch (Exception ex) 
            { 
                Logger.Write($"[PATH] FindBaseDir error: {ex.Message}", level: "ERROR");
            }
            Logger.Write($"[PATH] FindBaseDir fallback to current dir: {Directory.GetCurrentDirectory()}", level: "DEBUG");
            return Directory.GetCurrentDirectory();
        }
    }
}
