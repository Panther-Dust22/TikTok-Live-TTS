using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TTS.Shared.Utils;

namespace TTS.Shared.Utils
{
    public static class JsonUtil
    {
        public static T? LoadJson<T>(string path)
        {
            Logger.Write($"[JSONUTIL] LoadJson<{typeof(T).Name}> called for path: {path} at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                Logger.Write($"[JSONUTIL] Checking if file exists: {File.Exists(path)}");
                if (!File.Exists(path)) 
                {
                    Logger.Write($"[JSONUTIL] File does not exist, returning default");
                    return default;
                }
                Logger.Write($"[JSONUTIL] Reading file content at {DateTime.Now:HH:mm:ss.fff}");
                var json = File.ReadAllText(path, Encoding.UTF8);
                Logger.Write($"[JSONUTIL] File read successfully, length: {json.Length} characters");
                Logger.Write($"[JSONUTIL] Deserializing JSON at {DateTime.Now:HH:mm:ss.fff}");
                var result = JsonSerializer.Deserialize<T>(json);
                Logger.Write($"[JSONUTIL] JSON deserialized successfully at {DateTime.Now:HH:mm:ss.fff}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Write($"[JSONUTIL] ERROR in LoadJson: {ex.Message}", level: "ERROR");
                Logger.Write($"[JSONUTIL] Stack trace: {ex.StackTrace}", level: "ERROR");
                return default;
            }
        }

        public static void SaveJson<T>(T obj, string path)
        {
            Logger.Write($"[JSONUTIL] SaveJson<{typeof(T).Name}> called for path: {path} at {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                Logger.Write($"[JSONUTIL] Creating directory if needed: {Path.GetDirectoryName(path)}");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                
                Logger.Write($"[JSONUTIL] Serializing object at {DateTime.Now:HH:mm:ss.fff}");
                var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
                Logger.Write($"[JSONUTIL] Object serialized successfully, length: {json.Length} characters");
                
                Logger.Write($"[JSONUTIL] Writing to file at {DateTime.Now:HH:mm:ss.fff}");
                File.WriteAllText(path, json, Encoding.UTF8);
                Logger.Write($"[JSONUTIL] File written successfully at {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.Write($"[JSONUTIL] ERROR in SaveJson: {ex.Message}", level: "ERROR");
                Logger.Write($"[JSONUTIL] Stack trace: {ex.StackTrace}", level: "ERROR");
                throw;
            }
        }
    }
}
