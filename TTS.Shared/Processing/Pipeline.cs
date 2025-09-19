using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using TTS.Shared.Configuration;
using TTS.Shared.Utils;

namespace TTS.Shared.Processing
{
    public static class Pipeline
    {
        public sealed class Decision 
        { 
            public bool ShouldSpeak; 
            public string DisplayName = ""; 
            public string TextForTts = ""; 
            public string VoiceName = ""; 
            public float? Speed; 
            public string? SkipReason; 
        }

        public static Decision Process(Settings s, string nickname, string comment, bool isMod, bool isSub, string? gifterRank, int followRole)
        {
            var dec = new Decision { ShouldSpeak = false };
            string display = nickname;
            string text = comment;
            string originalNickname = nickname;
            var lowerComment = comment.ToLowerInvariant();

            // Priority voice
            string? voice = null; 
            float? speed = null;
            if (s.Users.C_priority_voice.TryGetValue(originalNickname, out var pv))
            {
                if (pv is JsonElement je)
                {
                    pv = je.ValueKind == JsonValueKind.String ? je.GetString()! : je;
                }
                if (pv is string vs)
                {
                    // Support combined format VOICE|SPEED
                    var bar = vs.IndexOf('|');
                    if (bar > 0)
                    {
                        voice = vs.Substring(0, bar).Trim();
                        var spStr = vs[(bar + 1)..].Trim();
                        if (NumberUtil.TryParseFloat(spStr, out var sp)) speed = sp;
                    }
                    else
                    {
                        voice = vs;
                    }
                }
                else if (pv is Dictionary<string, object> dictObj)
                {
                    foreach (var kv in dictObj)
                    {
                        voice = kv.Key; 
                        if (NumberUtil.TryParseFloat(kv.Value?.ToString(), out var sp)) speed = sp; 
                        break;
                    }
                }
                else if (pv is JsonElement mapEl && mapEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in mapEl.EnumerateObject())
                    {
                        voice = p.Name;
                        float sp;
                        if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            if (NumberUtil.TryParseFloat(p.Value.GetString(), out sp)) speed = sp;
                        }
                        else if (p.Value.ValueKind == JsonValueKind.Number)
                        {
                            try { sp = (float)p.Value.GetDouble(); speed = sp; } catch { }
                        }
                        break;
                    }
                }
            }
            
            // Priority branch: enforce A_ttscode after priority lookup
            if (voice != null)
            {
                if (s.Options.A_ttscode == "TRUE" && !text.StartsWith("!tts", StringComparison.OrdinalIgnoreCase))
                {
                    // Apply name swap for display before skipping
                    if (s.Users.E_name_swap.TryGetValue(originalNickname, out var swappedPri)) display = swappedPri;
                    dec.SkipReason = "A_ttscode=TRUE and message doesn't start with !tts";
                    dec.DisplayName = display;
                    return dec;
                }
                if (s.Options.A_ttscode == "TRUE" && text.StartsWith("!tts", StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(4).TrimStart();
                }

                // Bad word filter (use original lowerComment)
                foreach (var w in s.Filter.B_word_filter)
                {
                    if (!string.IsNullOrWhiteSpace(w) && lowerComment.Contains(w.ToLowerInvariant()))
                    {
                        var reply = s.Filter.B_filter_reply.Count > 0 ? s.Filter.B_filter_reply[new Random().Next(s.Filter.B_filter_reply.Count)] : "is trying to make me say a bad word";
                        text = ", " + reply;
                        s.Options.D_voice_map.TryGetValue("BadWordVoice", out var badVoice);
                        voice = badVoice ?? voice;
                        if (s.Users.E_name_swap.TryGetValue(originalNickname, out var swappedBad)) display = swappedBad;
                        dec.ShouldSpeak = true;
                        dec.DisplayName = display;
                        dec.TextForTts = text;
                        dec.VoiceName = voice;
                        // Bad-word responses should use default playback speed from config.json (player), not per-user speed
                        dec.Speed = null;
                        return dec;
                    }
                }

                // Name swap (for display)
                if (s.Users.E_name_swap.TryGetValue(originalNickname, out var swappedPri2)) display = swappedPri2;

                // Fallback if voice unresolved
                if (string.IsNullOrWhiteSpace(voice))
                {
                    if (!s.Options.D_voice_map.TryGetValue("Default", out voice) || string.IsNullOrWhiteSpace(voice))
                    {
                        voice = "EN_US_MALE_1";
                    }
                }
                Console.WriteLine($"[DEC] priority voice={voice} speed={(speed?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-")}");
                dec.ShouldSpeak = true;
                dec.DisplayName = display;
                dec.TextForTts = text;
                dec.VoiceName = voice;
                dec.Speed = speed;
                return dec;
            }

            // Non-priority branch
            // Name swap for display
            if (s.Users.E_name_swap.TryGetValue(originalNickname, out var swappedNon)) display = swappedNon;

            // A_ttscode enforcement
            if (s.Options.A_ttscode == "TRUE")
            {
                if (!text.StartsWith("!tts", StringComparison.OrdinalIgnoreCase))
                {
                    dec.SkipReason = "A_ttscode=TRUE and message doesn't start with !tts";
                    dec.DisplayName = display;
                    return dec;
                }
                text = text.Substring(4).TrimStart();
            }

            // Bad word filter
            foreach (var w in s.Filter.B_word_filter)
            {
                if (!string.IsNullOrWhiteSpace(w) && lowerComment.Contains(w.ToLowerInvariant()))
                {
                    var reply = s.Filter.B_filter_reply.Count > 0 ? s.Filter.B_filter_reply[new Random().Next(s.Filter.B_filter_reply.Count)] : "is trying to make me say a bad word";
                    text = ", " + reply;
                    s.Options.D_voice_map.TryGetValue("BadWordVoice", out var badVoice);
                    voice = badVoice;
                    if (string.IsNullOrWhiteSpace(voice))
                    {
                        if (!s.Options.D_voice_map.TryGetValue("Default", out voice) || string.IsNullOrWhiteSpace(voice))
                        {
                            voice = "EN_US_MALE_1";
                        }
                    }
                    dec.ShouldSpeak = true;
                    dec.DisplayName = display;
                    dec.TextForTts = text;
                    dec.VoiceName = voice;
                    // Bad-word responses: force default playback speed (handled in player)
                    dec.Speed = null;
                    return dec;
                }
            }

            // Role-based mapping if no priority
            if (voice == null)
            {
                if (isSub && s.Options.D_voice_map.TryGetValue("Subscriber", out var v1)) voice = v1;
                if (voice == null && isMod && s.Options.D_voice_map.TryGetValue("Moderator", out var v2)) voice = v2;
                if (voice == null && !string.IsNullOrWhiteSpace(gifterRank))
                {
                    if (int.TryParse(gifterRank, out var gr) && gr >= 1 && gr <= 5)
                    {
                        s.Options.D_voice_map.TryGetValue($"Top Gifter {gr}", out voice);
                    }
                }
                if (voice == null)
                {
                    s.Options.D_voice_map.TryGetValue($"Follow Role {followRole}", out voice);
                }
                if (voice == null) s.Options.D_voice_map.TryGetValue("Default", out voice);
            }
            Console.WriteLine($"[PIPE] Role map: isSub={isSub} isMod={isMod} followRole={followRole} gifterRank={gifterRank} -> voice={voice}");

            if (voice == "NONE") { dec.SkipReason = "Voice set to NONE"; dec.DisplayName = display; return dec; }

            if (string.IsNullOrWhiteSpace(voice))
            {
                if (!s.Options.D_voice_map.TryGetValue("Default", out voice) || string.IsNullOrWhiteSpace(voice))
                {
                    voice = "EN_US_MALE_1";
                }
            }
            dec.ShouldSpeak = true;
            dec.DisplayName = display;
            dec.TextForTts = text;
            dec.VoiceName = voice;
            dec.Speed = null;
            return dec;
        }
    }

    public static class NumberUtil
    {
        public static bool TryParseFloat(string? input, out float value)
        {
            Logger.Write($"[TRYPARSEFLOAT] TryParseFloat called with input='{input}' at {DateTime.Now:HH:mm:ss.fff}");
            value = 0f;
            if (string.IsNullOrWhiteSpace(input)) 
            {
                Logger.Write($"[TRYPARSEFLOAT] Input is null/whitespace, returning false");
                return false;
            }
            var result = float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            Logger.Write($"[TRYPARSEFLOAT] Parse result: {result}, value: {value}");
            return result;
        }
    }
}
