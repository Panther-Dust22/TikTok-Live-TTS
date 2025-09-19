using System;
using System.Collections.Generic;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class Voices
    {
        public Dictionary<string, List<VoiceItem>> Voice_List_cheat_sheet { get; set; } = new();
        
        public static Voices Load() 
        {
            Logger.Write($"[VOICES] Voices.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                var path = Settings.ResolveDataPath("voices.json");
                Logger.Write($"[VOICES] Resolved path: {path}");
                var result = JsonUtil.LoadJson<Voices>(path) ?? new Voices();
                Logger.Write($"[VOICES] Voices loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Write($"[VOICES] ERROR loading Voices: {ex.Message}", level: "ERROR");
                Logger.Write($"[VOICES] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
        }
    }

    public sealed class VoiceItem 
    { 
        public string name { get; set; } = ""; 
        public string code { get; set; } = ""; 
    }
}
