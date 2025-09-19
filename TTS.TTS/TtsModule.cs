using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TTS.Shared.Interfaces;
using TTS.Shared.Models;
using TTS.Shared.Utils;
using TTS.Shared.Configuration;

namespace TTS.Tts
{
    public class TtsModule : IModule
    {
        public string Name => "TTS";
        public bool IsRunning { get; private set; }

        private CancellationTokenSource? _cancellationTokenSource;
        private TtsEngine? _engine;
        private TTS.AudioQueue.AudioQueueModule? _audioQueueModule;

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

        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            Logger.Write("[TTS] Starting TTS module");
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Load TTS configuration
            var configPath = Path.Combine(GetBaseDirectory(), "data", "TTSconfig.json");
            var config = TtsConfig.Load(configPath);
            _engine = new TtsEngine(config, true); // Enable debug mode
            
            IsRunning = true;
            Logger.Write("[TTS] TTS module started successfully");
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
                return;

            Logger.Write("[TTS] Stopping TTS module");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _engine = null;

            IsRunning = false;
            Logger.Write("[TTS] TTS module stopped");
        }

        public void SetAudioQueue(TTS.AudioQueue.AudioQueueModule audioQueueModule)
        {
            _audioQueueModule = audioQueueModule;
        }

        public async Task ProcessTtsRequest(TtsRequest request)
        {
            if (_engine == null)
            {
                Logger.Write("[TTS] TTS engine not initialized", "ERROR");
                return;
            }

            Logger.Write($"[TTS] Processing TTS request - Voice: {request.Voice}, Text: {request.Text}");
            
            try
            {
                var voice = VoiceHelper.FromString(request.Voice);
                if (voice is null)
                {
                    Logger.Write($"[TTS] Invalid voice: {request.Voice}", "ERROR");
                    return;
                }

                Logger.Write($"[TTS] Processing request: voice={request.Voice} textLen={request.Text.Length}");
                
                // Generate TTS audio
                var mp3Chunks = await _engine.GenerateMp3ChunksAsync(request.Text, voice);
                if (mp3Chunks is null || mp3Chunks.Count == 0)
                {
                    Logger.Write("[TTS] Generation failed", "ERROR");
                    return;
                }

                // Combine all chunks into a single audio stream
                var totalLength = mp3Chunks.Sum(chunk => chunk.Length);
                var combinedAudio = new byte[totalLength];
                var offset = 0;
                
                foreach (var chunk in mp3Chunks)
                {
                    Buffer.BlockCopy(chunk, 0, combinedAudio, offset, chunk.Length);
                    offset += chunk.Length;
                }
                
                // Get default speed from config if no speed specified
                var defaultSpeed = 1.0f;
                try
                {
                    var configPath = Settings.ResolveDataPath("config.json");
                    Logger.Write($"[TTS] Looking for config at: {configPath}");
                    if (File.Exists(configPath))
                    {
                        var configJson = File.ReadAllText(configPath);
                        Logger.Write($"[TTS] Config JSON content: {configJson}");
                        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
                        if (config != null && config.TryGetValue("playback_speed", out var speedObj))
                        {
                            Logger.Write($"[TTS] Found playback_speed: {speedObj} (type: {speedObj.GetType()})");
                            if (speedObj is JsonElement speedElement && speedElement.ValueKind == JsonValueKind.Number)
                            {
                                defaultSpeed = (float)speedElement.GetDouble();
                                Logger.Write($"[TTS] Using default speed from config: {defaultSpeed}");
                            }
                        }
                        else
                        {
                            Logger.Write($"[TTS] playback_speed not found in config", "WARN");
                        }
                    }
                    else
                    {
                        Logger.Write($"[TTS] Config file not found at: {configPath}", "WARN");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write($"Error loading default speed from config: {ex.Message}", "WARN");
                }

                // Send combined audio as single AudioItem
                var audioItem = new AudioItem
                {
                    Data = combinedAudio,
                    SampleRate = 44100,
                    Channels = 1,
                    Speed = request.Speed ?? defaultSpeed,
                    Volume = 1.0f,
                    Text = request.Text,
                    Voice = request.Voice
                };
                
                Logger.Write($"[TTS] Enqueuing combined audio item (bytes={combinedAudio.Length}, chunks={mp3Chunks.Count}, speed={audioItem.Speed:0.##})");
                
                if (_audioQueueModule != null)
                {
                    _audioQueueModule.EnqueueAudio(audioItem);
                }
                else
                {
                    Logger.Write("[TTS] AudioQueue module not available", "ERROR");
                }

                Logger.Write("[TTS] Request completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Write($"Error in TTS processing: {ex.Message}", "ERROR");
                Logger.Gui($"TTS Error: {ex.Message}");
            }
        }
    }

    internal sealed class TtsConfig
    {
        [JsonPropertyName("request_timeout")] public int RequestTimeoutSeconds { get; set; } = 8;
        [JsonPropertyName("max_retries")] public int MaxRetries { get; set; } = 1;
        [JsonPropertyName("retry_delay")] public double RetryDelaySeconds { get; set; } = 0.2;
        [JsonPropertyName("playback_speed")] public double PlaybackSpeed { get; set; } = 1.0;
        [JsonPropertyName("max_concurrent_requests")] public int MaxConcurrentRequests { get; set; } = 5;
        [JsonPropertyName("performance_test_enabled")] public bool PerformanceTestEnabled { get; set; } = true;
        [JsonPropertyName("performance_test_text")] public string PerformanceTestText { get; set; } = "Hello world, this is a test message.";
        [JsonPropertyName("performance_test_voice")] public string PerformanceTestVoice { get; set; } = "EN_US_MALE_1";
        [JsonPropertyName("performance_test_timeout")] public int PerformanceTestTimeoutSeconds { get; set; } = 10;
        [JsonPropertyName("performance_test_retries")] public int PerformanceTestRetries { get; set; } = 2;
        [JsonPropertyName("api_endpoints")] public List<ApiEndpoint>? ApiEndpoints { get; set; } = new();

        public static TtsConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<TtsConfig>(json, JsonOptions()) ?? new TtsConfig();
                    return cfg;
                }
            }
            catch
            {
                // ignore and return defaults
            }
            return new TtsConfig();
        }

        public List<ApiEndpoint> EffectiveEndpoints()
        {
            if (ApiEndpoints != null && ApiEndpoints.Count > 0) return ApiEndpoints;
            return new List<ApiEndpoint>
            {
                new ApiEndpoint { Url = "https://tiktok-tts.weilnet.workers.dev/api/generation", Response = "data", Name = "weilnet" },
                new ApiEndpoint { Url = "https://gesserit.co/api/tiktok-tts", Response = "base64", Name = "gesserit" },
                new ApiEndpoint { Url = "https://tiktok-tts.weilnet.workers.dev/api/generation", Response = "data", Name = "weilnet_backup" },
            };
        }

        internal static JsonSerializerOptions JsonOptions() => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    internal sealed class ApiEndpoint
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("response")] public string Response { get; set; } = "data";
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    internal static class VoiceHelper
    {
        private static Dictionary<string, string>? _cache;

        private static string? ResolveVoicesPath()
        {
            try
            {
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    var p = Path.Combine(dir.FullName, "data", "voices.json");
                    if (File.Exists(p)) return p;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        private static Dictionary<string, string> LoadMap()
        {
            if (_cache != null) return _cache;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = ResolveVoicesPath();
                if (path != null)
                {
                    using var fs = File.OpenRead(path);
                    using var doc = JsonDocument.Parse(fs);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Voice_List_cheat_sheet", out var sections))
                    {
                        foreach (var section in sections.EnumerateObject())
                        {
                            if (section.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in section.Value.EnumerateArray())
                                {
                                    if (item.TryGetProperty("name", out var nameEl) && item.TryGetProperty("code", out var codeEl))
                                    {
                                        var name = nameEl.GetString() ?? string.Empty;
                                        var code = codeEl.GetString() ?? string.Empty;
                                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(code))
                                        {
                                            map[name] = code;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            _cache = map;
            return map;
        }

        public static string? FromString(string input)
        {
            var map = LoadMap();
            if (map.TryGetValue(input, out var v)) return v;
            return null;
        }
    }

    internal sealed class TtsEngine
    {
        private readonly TtsConfig _config;
        private readonly HttpClient _http;
        private readonly Dictionary<string, int> _endpointFailures = new();
        private int _apiCalls;
        private int _totalRequests;
        private int _failedRequests;
        private readonly bool _debug;

        public TtsEngine(TtsConfig config, bool debug = false)
        {
            _config = config;
            _debug = debug;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _config.RequestTimeoutSeconds))
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TTS.CS/1.0 (+https://localhost)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<List<byte[]>?> GenerateMp3ChunksAsync(string text, string voice)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text cannot be empty");
            if (string.IsNullOrWhiteSpace(voice)) throw new ArgumentException("Voice must be provided");

            _totalRequests++;

            var chunks = SplitText(text);
            var results = new byte[chunks.Count][];

            using var throttler = new SemaphoreSlim(Math.Max(1, _config.MaxConcurrentRequests));
            var tasks = chunks.Select(async (chunk, index) =>
            {
                await throttler.WaitAsync();
                try
                {
                    var b64 = await TryAllEndpointsWithRetryAsync(chunk, voice);
                    if (b64 is null) return; // leave null
                    results[index] = Convert.FromBase64String(b64);
                }
                finally
                {
                    throttler.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            if (results.Any(r => r is null))
            {
                _failedRequests++;
                return null;
            }

            return results.ToList();
        }

        private async Task<string?> TryAllEndpointsWithRetryAsync(string text, string voice)
        {
            var endpoints = _config.EffectiveEndpoints();
            for (int attempt = 0; attempt < Math.Max(1, _config.MaxRetries); attempt++)
            {
                foreach (var ep in endpoints)
                {
                    var result = await MakeSingleRequestAsync(ep, text, voice);
                    if (result is not null) return result;
                }
                if (attempt < _config.MaxRetries - 1)
                {
                    var delay = TimeSpan.FromSeconds(_config.RetryDelaySeconds * (attempt + 1));
                    await Task.Delay(delay);
                }
            }
            return null;
        }

        private async Task<string?> MakeSingleRequestAsync(ApiEndpoint endpoint, string text, string voice)
        {
            try
            {
                var body = JsonSerializer.Serialize(new { text, voice });
                using var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                payload.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                if (_debug) Logger.Write($"[DEBUG] POST {endpoint.Name} {endpoint.Url} voice={voice} text='{text.Substring(0, Math.Min(40, text.Length))}'...");
                using var resp = await _http.PostAsync(endpoint.Url, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var respBody = await resp.Content.ReadAsStringAsync();
                    if (_debug) Logger.Write($"[DEBUG] {endpoint.Name} HTTP {(int)resp.StatusCode}: {Truncate(respBody, 160)}");
                    return null;
                }
                _apiCalls++;
                using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);
                if (doc.RootElement.TryGetProperty(endpoint.Response, out var val))
                {
                    var str = val.GetString();
                    if (_debug) Logger.Write($"[DEBUG] {endpoint.Name} OK len={str?.Length ?? 0}");
                    return str;
                }
                if (_debug) Logger.Write($"[DEBUG] {endpoint.Name} missing field '{endpoint.Response}'");
                return null;
            }
            catch
            {
                if (_debug) Logger.Write($"[DEBUG] {endpoint.Name} failed");
                if (!_endpointFailures.ContainsKey(endpoint.Name)) _endpointFailures[endpoint.Name] = 0;
                _endpointFailures[endpoint.Name]++;
                return null;
            }
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static List<string> SplitText(string text)
        {
            var chunks = new List<string>();
            var idx = text.IndexOf(" says ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var user = text.Substring(0, idx);
                var comment = text[(idx + 6)..];
                var first = $"{user} says";
                chunks.Add(first);
                var trimmed = comment.Trim();
                if (!string.IsNullOrEmpty(trimmed)) chunks.Add(trimmed);
                return chunks;
            }
            // Fallback: simple sentence-like split
            chunks = RegexSplit(text);
            return chunks.Count == 0 ? new List<string> { text } : chunks;
        }

        private static List<string> RegexSplit(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                sb.Append(ch);
                if (ch == '.' || ch == '!' || ch == '?' || ch == ':' || ch == ';' || ch == '-')
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }
    }

    internal static class AudioQueueClient
    {
        public static bool TrySend(byte[] audioData)
        {
            try
            {
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", "audio_queue_pipe", System.IO.Pipes.PipeDirection.Out);
                client.Connect(500); // 500ms
                using var bw = new BinaryWriter(client);
                bw.Write(audioData.Length);
                bw.Write(audioData);
                bw.Flush();
                Logger.Write($"[TTS->AQ] sent bytes={audioData.Length}");
                return true;
            }
            catch
            {
                Logger.Write("[TTS->AQ] send failed", "ERROR");
                return false;
            }
        }

        public static byte[] WrapWithSpeedHeader(byte[] audioData, float speed)
        {
            // Payload layout: 'AQ2S'(4) | version(4,int32=1) | speed(4,float32) | data(N)
            var header = new byte[12];
            header[0] = (byte)'A'; header[1] = (byte)'Q'; header[2] = (byte)'2'; header[3] = (byte)'S';
            BitConverter.GetBytes(1).CopyTo(header, 4);
            BitConverter.GetBytes(speed).CopyTo(header, 8);
            var payload = new byte[header.Length + audioData.Length];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            Buffer.BlockCopy(audioData, 0, payload, header.Length, audioData.Length);
            return payload;
        }

    }
}
