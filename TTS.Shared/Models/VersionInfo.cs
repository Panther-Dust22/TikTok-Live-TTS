using System.Reflection;
using System.Text.Json;

namespace TTS.Shared.Models
{
    public class VersionInfo
    {
        public string Version { get; set; } = "v4.6";
        public string DisplayName { get; set; } = "TTS Voice System";
        public string FullTitle { get; set; } = "TTS Voice System V4.6";
        public string Subtitle { get; set; } = "Modular Architecture - Created by Emstar233 & Husband";
        public string BuildDate { get; set; } = "2025-01-17";
        public string Description { get; set; } = "TikTok Live TTS BSR Injector - Modular C# Version";

        public static VersionInfo Load()
        {
            try
            {
                // Try to load from assembly first (secure, tamper-proof)
                // Get the main application assembly, not the shared library assembly
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "TTS Voice System";
                var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Emstar233 & Husband";
                var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "TikTok Live TTS BSR Injector - Modular C# Version";
                var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © 2025";

                if (version != null)
                {
                    var versionString = $"v{version.Major}.{version.Minor}";
                    return new VersionInfo
                    {
                        Version = versionString,
                        DisplayName = product,
                        FullTitle = $"{product} {versionString}",
                        Subtitle = $"Modular Architecture - Created by {company}",
                        BuildDate = File.GetCreationTime(assembly.Location).ToString("yyyy-MM-dd"),
                        Description = description
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading version from assembly: {ex.Message}");
            }

            // Fallback to JSON file if assembly loading fails
            try
            {
                var versionFile = Path.Combine(GetBaseDirectory(), "data", "VersionInfo.json");
                if (File.Exists(versionFile))
                {
                    var json = File.ReadAllText(versionFile);
                    var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json);
                    return versionInfo ?? new VersionInfo();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading VersionInfo.json: {ex.Message}");
            }
            
            return new VersionInfo();
        }

        public void Save()
        {
            // Note: This method is kept for compatibility but won't actually save
            // since we're now using assembly version for security
            Console.WriteLine("Version info is now read from assembly - cannot be modified at runtime");
        }

        private static string GetBaseDirectory()
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
    }
}
