using System;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class AppConfig
    {
        public float playback_speed { get; set; } = 1.0f;
        
        public static AppConfig Load() 
        {
            Logger.Write($"[APPCONFIG] AppConfig.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                var path = Settings.ResolveDataPath("config.json");
                Logger.Write($"[APPCONFIG] Resolved path: {path}");
                var result = JsonUtil.LoadJson<AppConfig>(path) ?? new AppConfig();
                Logger.Write($"[APPCONFIG] AppConfig loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Write($"[APPCONFIG] ERROR loading AppConfig: {ex.Message}", level: "ERROR");
                Logger.Write($"[APPCONFIG] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
        }
    }
}
