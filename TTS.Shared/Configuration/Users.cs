using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Configuration
{
    public sealed class Users
    {
        public Dictionary<string, object> C_priority_voice { get; set; } = new();
        public Dictionary<string, string> E_name_swap { get; set; } = new();
        
        public static Users Load()
        {
            Logger.Write($"[USERS] Users.Load() called at {DateTime.Now:HH:mm:ss.fff}");
            var u = new Users();
            try
            {
                var path = Settings.ResolveDataPath("user_management.json");
                Logger.Write($"[USERS] Resolved path: {path}");
                u = JsonUtil.LoadJson<Users>(path) ?? new Users();
                Logger.Write($"[USERS] Users loaded from JSON, processing priority voice map at {DateTime.Now:HH:mm:ss.fff}");
                Logger.Write($"[USERS] Priority voice map count: {u.C_priority_voice?.Count ?? 0}");
                
                var fixedMap = new Dictionary<string, object>(StringComparer.Ordinal);
                Logger.Write($"[USERS] Starting to process priority voice entries at {DateTime.Now:HH:mm:ss.fff}");
                foreach (var kv in u.C_priority_voice)
                {
                    Logger.Write($"[USERS] Processing key: '{kv.Key}', value: '{kv.Value}'");
                    var key = kv.Key;
                    var val = kv.Value;
                    if (key.Contains(' ') && val is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        Logger.Write($"[USERS] Key contains space, processing heuristic at {DateTime.Now:HH:mm:ss.fff}");
                        // Heuristic: last token is VOICE (all uppercase/underscore), preceding is username
                        var lastSpace = key.LastIndexOf(' ');
                        if (lastSpace > 0)
                        {
                            var name = key.Substring(0, lastSpace);
                            var voice = key.Substring(lastSpace + 1);
                            Logger.Write($"[USERS] Split into name: '{name}', voice: '{voice}'");
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(voice) && NameUtil.IsAllUpperOrUnderscore(voice))
                            {
                                Logger.Write($"[USERS] Voice is all upper/underscore, adding to fixed map");
                                fixedMap[name] = new Dictionary<string, object> { { voice, s } };
                                continue;
                            }
                        }
                    }
                    Logger.Write($"[USERS] Adding to fixed map as-is: key='{key}', value='{val}'");
                    fixedMap[key] = val;
                }
                Logger.Write($"[USERS] Finished processing priority voice entries, updating map at {DateTime.Now:HH:mm:ss.fff}");
                u.C_priority_voice = fixedMap;
                Logger.Write($"[USERS] Users.Load() completed successfully at {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.Write($"[USERS] ERROR in Users.Load(): {ex.Message}", level: "ERROR");
                Logger.Write($"[USERS] Stack trace: {ex.StackTrace}", level: "ERROR");
            }
            return u;
        }

        public void Save()
        {
            var path = Settings.ResolveDataPath("user_management.json");
            var dir = Path.GetDirectoryName(path)!; 
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            }), Encoding.UTF8);
        }
    }

    public static class NameUtil
    {
        public static bool IsAllUpperOrUnderscore(string input)
        {
            foreach (var ch in input)
            {
                if (!(char.IsUpper(ch) || ch == '_' || char.IsDigit(ch))) return false;
            }
            return input.Length > 0;
        }
    }
}
