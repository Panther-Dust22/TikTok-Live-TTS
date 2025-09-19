using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TTS.Shared.Configuration;
using TTS.Shared.Utils;

namespace TTS.Shared.Processing
{
    public static class CommandHandlers
    {
        public static bool TryHandle(Settings s, string nickname, string comment, bool isModerator)
        {
            // Only moderators and when Voice_change.Enabled == TRUE can modify files
            if (!s.Options.Voice_change.Enabled.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return false;
            if (!isModerator) return true; // treat as handled but noop so it won't reach TTS
            // Note: restart commands are handled by ESD daemon, not here

            try
            {
                if (comment.StartsWith("!vadd", StringComparison.OrdinalIgnoreCase))
                {
                    // !vadd name with spaces VOICE [speed]
                    var parts = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3) // Need at least: !vadd name VOICE
                    {
                        string voice = null!; 
                        float? speed = null; 
                        string target = "";
                        
                        // Primary: assume last token is speed if numeric, token before is voice
                        int voiceIdx = -1; 
                        int speedIdx = -1;
                        if (parts.Length >= 4 && NumberUtil.TryParseFloat(parts[^1], out var sp1)) 
                        { 
                            speed = sp1; 
                            speedIdx = parts.Length - 1; 
                            voiceIdx = parts.Length - 2; 
                        }
                        else 
                        { 
                            voiceIdx = parts.Length - 1; 
                        }
                        voice = parts[voiceIdx];
                        target = string.Join(' ', parts.AsSpan(1, voiceIdx - 1).ToArray()).TrimStart('@');
                        
                        // Fallback if mis-detected (voice too short): scan for last ALLCAPS token
                        if (string.IsNullOrWhiteSpace(target) || voice.Length <= 1)
                        {
                            for (int i = parts.Length - 1; i >= 1; i--)
                            {
                                if (parts[i].ToUpperInvariant() == parts[i] && parts[i].Length > 1)
                                {
                                    voice = parts[i];
                                    if (i + 1 < parts.Length && NumberUtil.TryParseFloat(parts[i + 1], out var sp2)) speed = sp2;
                                    target = string.Join(' ', parts[1..i]).TrimStart('@');
                                    break;
                                }
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(voice) && !string.IsNullOrEmpty(target))
                        {
                            if (speed.HasValue)
                            {
                                // Speed specified: save with speed
                                s.Users.C_priority_voice[target] = new Dictionary<string, object> { { voice, speed.Value.ToString("0.###", CultureInfo.InvariantCulture) } };
                                Logger.Write($"!vadd by {nickname}: {target} -> {voice} @ {speed.Value.ToString("0.###")}x");
                                Logger.Gui($"!vadd by {nickname}: {target} -> {voice} @ {speed.Value.ToString("0.###")}x");
                            }
                            else
                            {
                                // No speed specified: save without speed (will use default at playback time)
                                s.Users.C_priority_voice[target] = voice;
                                Logger.Write($"!vadd by {nickname}: {target} -> {voice} (using default speed)");
                                Logger.Gui($"!vadd by {nickname}: {target} -> {voice} (using default speed)");
                            }
                            s.Users.Save();
                            Logger.Gui("settings updated confirmation");
                        }
                    }
                    return true;
                }
                
                if (comment.StartsWith("!vremove", StringComparison.OrdinalIgnoreCase))
                {
                    var target = comment.Substring(8).Trim().TrimStart('@');
                    if (!string.IsNullOrEmpty(target) && s.Users.C_priority_voice.ContainsKey(target))
                    {
                        s.Users.C_priority_voice.Remove(target);
                        s.Users.Save();
                        Logger.Write($"!vremove by {nickname}: {target}");
                        Logger.Gui($"!vremove by {nickname}: {target}");
                        Logger.Gui("settings updated confirmation");
                    }
                    return true;
                }
                
                if (comment.StartsWith("!vchange", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        string voice = null!; 
                        float? speed = null; 
                        string target = "";
                        int voiceIdx = -1; 
                        int speedIdx = -1;
                        if (parts.Length >= 4 && NumberUtil.TryParseFloat(parts[^1], out var sp1)) 
                        { 
                            speed = sp1; 
                            speedIdx = parts.Length - 1; 
                            voiceIdx = parts.Length - 2; 
                        }
                        else 
                        { 
                            voiceIdx = parts.Length - 1; 
                        }
                        voice = parts[voiceIdx];
                        target = string.Join(' ', parts.AsSpan(1, voiceIdx - 1).ToArray()).TrimStart('@');
                        if (string.IsNullOrWhiteSpace(target) || voice.Length <= 1)
                        {
                            for (int i = parts.Length - 1; i >= 1; i--)
                            {
                                if (parts[i].ToUpperInvariant() == parts[i] && parts[i].Length > 1)
                                {
                                    voice = parts[i]; 
                                    if (i + 1 < parts.Length && NumberUtil.TryParseFloat(parts[i + 1], out var sp2)) speed = sp2;
                                    target = string.Join(' ', parts[1..i]).TrimStart('@');
                                    break;
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(voice) && !string.IsNullOrEmpty(target))
                        {
                            if (speed.HasValue)
                            {
                                // Speed specified: save with speed
                                s.Users.C_priority_voice[target] = new Dictionary<string, object> { { voice, speed.Value.ToString("0.###", CultureInfo.InvariantCulture) } };
                                Logger.Write($"!vchange by {nickname}: {target} -> {voice} @ {speed.Value.ToString("0.###")}x");
                                Logger.Gui($"!vchange by {nickname}: {target} -> {voice} @ {speed.Value.ToString("0.###")}x");
                            }
                            else
                            {
                                // No speed specified: save without speed (will use default at playback time)
                                s.Users.C_priority_voice[target] = voice;
                                Logger.Write($"!vchange by {nickname}: {target} -> {voice} (using default speed)");
                                Logger.Gui($"!vchange by {nickname}: {target} -> {voice} (using default speed)");
                            }
                            s.Users.Save();
                            Logger.Gui("settings updated confirmation");
                        }
                    }
                    return true;
                }
                
                if (comment.StartsWith("!vname", StringComparison.OrdinalIgnoreCase))
                {
                    // !vname original name - new display name
                    var idx = comment.IndexOf(" - ", StringComparison.Ordinal);
                    if (idx > 0)
                    {
                        var original = comment.Substring(6, idx - 6).Trim().TrimStart('@');
                        var newName = comment.Substring(idx + 3).Trim();
                        if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(newName))
                        {
                            s.Users.E_name_swap[original] = newName;
                            s.Users.Save();
                            Logger.Write($"!vname by {nickname}: {original} -> {newName}");
                            Logger.Gui($"!vname by {nickname}: {original} -> {newName}");
                            Logger.Gui("settings updated confirmation");
                        }
                    }
                    return true;
                }
                
                if (comment.StartsWith("!vnoname", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var user = parts[1].TrimStart('@');
                        if (s.Users.E_name_swap.ContainsKey(user))
                        {
                            s.Users.E_name_swap.Remove(user); 
                            s.Users.Save();
                            Logger.Write($"!vnoname by {nickname}: {user}");
                            Logger.Gui($"!vnoname by {nickname}: {user}");
                            Logger.Gui("settings updated confirmation");
                        }
                    }
                    return true;
                }
                
                if (comment.StartsWith("!vrude", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        // Load full filter.json to preserve other keys
                        var filterPath = Settings.ResolveDataPath("filter.json");
                        Dictionary<string, object> filterDoc;
                        try
                        {
                            filterDoc = File.Exists(filterPath)
                                ? JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(filterPath, Encoding.UTF8)) ?? new()
                                : new();
                        }
                        catch { filterDoc = new(); }

                        var current = new List<string>();
                        if (filterDoc.TryGetValue("B_word_filter", out var arrObj))
                        {
                            try
                            {
                                if (arrObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var el in je.EnumerateArray()) 
                                    { 
                                        var sVal = el.GetString(); 
                                        if (!string.IsNullOrEmpty(sVal)) current.Add(sVal); 
                                    }
                                }
                            }
                            catch { }
                        }
                        var set = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                        for (int i = 1; i < parts.Length; i++) 
                        { 
                            var w = parts[i]; 
                            if (!string.IsNullOrWhiteSpace(w) && !set.Contains(w)) 
                            { 
                                current.Add(w); 
                                set.Add(w); 
                            } 
                        }
                        filterDoc["B_word_filter"] = current;

                        // Write back full filter.json (UTF-8, no escaping for apostrophes)
                        Directory.CreateDirectory(Path.GetDirectoryName(filterPath)!);
                        File.WriteAllText(filterPath, JsonSerializer.Serialize(filterDoc, new JsonSerializerOptions 
                        { 
                            WriteIndented = true, 
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                        }), Encoding.UTF8);
                        // Reflect in-memory structure too
                        s.Filter.B_word_filter = current;
                        Logger.Write($"!vrude by {nickname}: added {string.Join(", ", parts[1..])}");
                        Logger.Gui($"!vrude by {nickname}: added {string.Join(", ", parts[1..])}");
                        Logger.Gui("settings updated confirmation");
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
