using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class FilterConfig
    {
        public List<string> B_word_filter { get; set; } = new();
        public List<string> B_filter_reply { get; set; } = new();
        
        public static FilterConfig Load() 
        {
            Logger.Write($"[FILTER] FilterConfig.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                var path = Settings.ResolveDataPath("filter.json");
                Logger.Write($"[FILTER] Resolved path: {path}");
                var result = JsonUtil.LoadJson<FilterConfig>(path) ?? new FilterConfig();
                Logger.Write($"[FILTER] FilterConfig loaded successfully at {DateTime.Now:HH:mm:ss.fff}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Write($"[FILTER] ERROR loading FilterConfig: {ex.Message}", level: "ERROR");
                Logger.Write($"[FILTER] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
        }

        public void Save()
        {
            var path = Settings.ResolveDataPath("filter.json");
            var dir = Path.GetDirectoryName(path)!; 
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            }), Encoding.UTF8);
        }
    }
}
