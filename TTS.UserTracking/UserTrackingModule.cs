using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TTS.Shared.Interfaces;
using TTS.Shared.Utils;
using TTS.Shared.Configuration;

namespace TTS.UserTracking
{
    public class UserTrackingModule : IModule
    {
        public string Name => "UserTracking";
        public bool IsRunning { get; private set; }

        private const string WsUrl = "ws://localhost:21213/";
        private const int ExpirySeconds = 300; // 5 minutes
        private const int SaveIntervalSeconds = 30;

        private readonly ConcurrentDictionary<string, double> _activeUsers = new(StringComparer.Ordinal);
        private DateTime _lastSave = DateTime.MinValue;
        private CancellationTokenSource? _cancellationTokenSource;
        private WebSocket? _webSocket;

        public event EventHandler<ActiveUsersChangedEventArgs>? ActiveUsersChanged;

        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            Logger.Write("[USERTRACKING] Starting UserTracking module");
            _cancellationTokenSource = new CancellationTokenSource();

            // Load existing active users
            LoadFromFile();

            // Start cleanup loop
            _ = Task.Run(CleanupLoop);

            // Start WebSocket connection
            _ = Task.Run(RunWebSocket);

            IsRunning = true;
            Logger.Write("[USERTRACKING] UserTracking module started successfully");
            await Task.CompletedTask; // Make it properly async
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
                return;

            Logger.Write("[USERTRACKING] Stopping UserTracking module");
            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }

            // Save final state
            SaveToFile();

            IsRunning = false;
            Logger.Write("[USERTRACKING] UserTracking module stopped");
        }

        private async Task RunWebSocket()
        {
            var backoff = 500;
            var buffer = new byte[32 * 1024];
            
            while (!_cancellationTokenSource?.Token.IsCancellationRequested ?? false)
            {
                using var ws = new ClientWebSocket();
                _webSocket = ws;
                
                try
                {
                    Logger.Write("[USERTRACKING] Connecting to WebSocket...");
                    await ws.ConnectAsync(new Uri(WsUrl), _cancellationTokenSource!.Token);
                    Logger.Write("[USERTRACKING] Connected to WebSocket");
                    backoff = 500;
                    
                    while (ws.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult res;
                        
                        do
                        {
                            res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
                            if (res.MessageType == WebSocketMessageType.Close) break;
                            if (res.Count > 0) ms.Write(buffer, 0, res.Count);
                        } while (!res.EndOfMessage);
                        
                        if (res.MessageType == WebSocketMessageType.Close) break;

                        var msg = Encoding.UTF8.GetString(ms.ToArray());
                        try
                        {
                            using var doc = JsonDocument.Parse(msg);
                            var root = doc.RootElement;
                            
                            if (root.TryGetProperty("event", out var ev) && 
                                ev.GetString() == "chat" && 
                                root.TryGetProperty("data", out var d))
                            {
                                var nickname = d.TryGetProperty("nickname", out var n) ? n.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(nickname))
                                {
                                    var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                    _activeUsers[nickname!] = nowSecs;
                                    
                                    // Notify GUI of user activity
                                    ActiveUsersChanged?.Invoke(this, new ActiveUsersChangedEventArgs
                                    {
                                        UserName = nickname!,
                                        Action = "active",
                                        TotalUsers = _activeUsers.Count
                                    });
                                    
                                    // Don't log user activity to reduce noise
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Write($"[USERTRACKING] Error parsing message: {ex.Message}", "WARN");
                        }

                        MaybeSave();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[USERTRACKING] WebSocket error: {ex.Message}", "ERROR");
                }
                finally 
                { 
                    try { ws.Abort(); } catch { }
                    _webSocket = null;
                }
                
                if (!_cancellationTokenSource?.Token.IsCancellationRequested ?? false)
                {
                    await Task.Delay(backoff, _cancellationTokenSource.Token);
                    backoff = Math.Min(backoff * 2, 10_000);
                }
            }
        }

        private async Task CleanupLoop()
        {
            while (!_cancellationTokenSource?.Token.IsCancellationRequested ?? false)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), _cancellationTokenSource!.Token);
                    var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var removedCount = 0;
                    
                    foreach (var kv in _activeUsers.ToArray())
                    {
                        if (nowSecs - kv.Value > ExpirySeconds)
                        {
                            if (_activeUsers.TryRemove(kv.Key, out _))
                            {
                                removedCount++;
                                
                                // Notify GUI of user removal
                                ActiveUsersChanged?.Invoke(this, new ActiveUsersChangedEventArgs
                                {
                                    UserName = kv.Key,
                                    Action = "expired",
                                    TotalUsers = _activeUsers.Count
                                });
                            }
                        }
                    }
                    
                    if (removedCount > 0)
                    {
                        Logger.Write($"[USERTRACKING] Removed {removedCount} expired users (Total: {_activeUsers.Count})");
                    }
                    
                    MaybeSave();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[USERTRACKING] Cleanup error: {ex.Message}", "ERROR");
                }
            }
        }

        private void LoadFromFile()
        {
            try
            {
                var path = Settings.ResolveDataPath("active_users.json");
                if (!File.Exists(path)) return;
                
                var json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("active_users", out var map) && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in map.EnumerateObject())
                    {
                        double ts;
                        if (prop.Value.ValueKind == JsonValueKind.Number) 
                            ts = prop.Value.GetDouble();
                        else if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var d)) 
                            ts = d;
                        else 
                            continue;
                            
                        _activeUsers[prop.Name] = ts;
                    }
                }
                
                Logger.Write($"[USERTRACKING] Loaded {_activeUsers.Count} active users from file");
            }
            catch (Exception ex)
            {
                Logger.Write($"[USERTRACKING] Error loading active users: {ex.Message}", "ERROR");
            }
        }

        private void MaybeSave()
        {
            if ((DateTime.UtcNow - _lastSave).TotalSeconds < SaveIntervalSeconds) return;
            SaveToFile();
        }

        private void SaveToFile()
        {
            try
            {
                _lastSave = DateTime.UtcNow;
                var path = Settings.ResolveDataPath("active_users.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                
                var payload = new
                {
                    active_users = _activeUsers.ToDictionary(k => k.Key, v => v.Value),
                    last_updated = DateTime.UtcNow.ToString("o"),
                    total_users = _activeUsers.Count
                };
                
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
                { 
                    WriteIndented = true, 
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                });
                
                File.WriteAllText(path, json, Encoding.UTF8);
                Logger.Write($"[USERTRACKING] Saved {_activeUsers.Count} active users to file");
            }
            catch (Exception ex)
            {
                Logger.Write($"[USERTRACKING] Save error: {ex.Message}", "ERROR");
            }
        }

        // Public methods for GUI access
        public int GetActiveUserCount() => _activeUsers.Count;
        
        public string[] GetActiveUsers()
        {
            var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return _activeUsers
                .Where(kv => nowSecs - kv.Value <= ExpirySeconds)
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Key)
                .ToArray();
        }
        
        public bool IsUserActive(string userName)
        {
            if (!_activeUsers.TryGetValue(userName, out var timestamp)) return false;
            var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return nowSecs - timestamp <= ExpirySeconds;
        }
    }

    public class ActiveUsersChangedEventArgs : EventArgs
    {
        public string UserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // "active" or "expired"
        public int TotalUsers { get; set; }
    }
}
