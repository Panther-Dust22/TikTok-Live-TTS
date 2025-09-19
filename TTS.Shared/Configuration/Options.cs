using System;
using System.Collections.Generic;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class Options
    {
        public string A_ttscode { get; set; } = "FALSE";
        public Dictionary<string, string> D_voice_map { get; set; } = new();
        public VoiceChange Voice_change { get; set; } = new();

        public static Options Load() 
        {
            Logger.Write($"[OPTIONS] Options.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                var path = Settings.ResolveDataPath("options.json");
                Logger.Write($"[OPTIONS] Resolved path: {path}");
                var result = JsonUtil.LoadJson<Options>(path) ?? new Options();
                Logger.Write($"[OPTIONS] Options loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                Logger.Write($"[OPTIONS] D_voice_map count: {result.D_voice_map?.Count ?? 0}");
                if (result.D_voice_map != null)
                {
                    foreach (var kvp in result.D_voice_map)
                    {
                        Logger.Write($"[OPTIONS] D_voice_map[{kvp.Key}] = '{kvp.Value}' (type: {kvp.Value?.GetType().Name})");
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Logger.Write($"[OPTIONS] ERROR loading Options: {ex.Message}", level: "ERROR");
                Logger.Write($"[OPTIONS] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
        }
    }

    public sealed class VoiceChange 
    { 
        public string Enabled { get; set; } = "FALSE"; 
    }
}
