using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TTS.Shared.Interfaces;
using TTS.Shared.Models;
using TTS.Shared.Services;
using TTS.Shared.Utils;
using TTS.Shared.Configuration;
using TTS.Shared.Processing;

namespace TTS.Processing
{
    public class ProcessingModule : IModule
    {
        public string Name => "Processing";
        public bool IsRunning { get; private set; }

        private readonly Uri _wsUri = new Uri("ws://localhost:21213/");
              private readonly ConcurrentQueue<ProcessingJob> _jobQueue = new();
              private readonly AutoResetEvent _jobSignal = new(false);
              private int _maxInFlight = 4;
              private CancellationTokenSource? _cancellationTokenSource;
              private INamedPipeService? _pipeService;
              private WebSocket? _webSocket;
        private Settings? _settings;
        private TTS.Tts.TtsModule? _ttsModule;
        private TTS.AudioQueue.AudioQueueModule? _audioQueueModule;
        private bool _emergencyStopActive = false;

        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            Logger.Write("[PROCESSING] Starting Processing module");
            _cancellationTokenSource = new CancellationTokenSource();
            
                  // Load settings
                  _settings = Settings.Load();
                  _maxInFlight = _settings.MaxInFlightJobs;
                  
                // Initialize AudioQueue module first
                _audioQueueModule = new TTS.AudioQueue.AudioQueueModule();
                await _audioQueueModule.StartAsync();
                
                // Initialize TTS module and connect it to AudioQueue
                _ttsModule = new TTS.Tts.TtsModule();
                _ttsModule.SetAudioQueue(_audioQueueModule);
                await _ttsModule.StartAsync();
                  
                  // Initialize pipe service
                  _pipeService = new NamedPipeService();
                  _pipeService.MessageReceived += OnPipeMessageReceived;
            
            // Start job worker
            _ = Task.Run(() => JobWorker(_cancellationTokenSource.Token));
            
            // Start WebSocket connection
            _ = Task.Run(() => RunWebSocket(_cancellationTokenSource.Token));
            
            IsRunning = true;
            Logger.Write("[PROCESSING] Processing module started successfully");
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
                return;

            Logger.Write("[PROCESSING] Stopping Processing module");
            _cancellationTokenSource?.Cancel();
            
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }
            
            _pipeService?.Dispose();
            if (_ttsModule != null)
            {
                await _ttsModule.StopAsync();
                _ttsModule = null;
            }
            if (_audioQueueModule != null)
            {
                await _audioQueueModule.StopAsync();
                _audioQueueModule = null;
            }
                  IsRunning = false;
                  Logger.Write("[PROCESSING] Processing module stopped");
        }

        public void SetEmergencyStopState(bool isActive)
        {
            _emergencyStopActive = isActive;
            Logger.Write($"[PROCESSING] Emergency stop state set to: {isActive}");
        }

        public int GetQueueCount()
        {
            return _jobQueue.Count;
        }

        private void OnPipeMessageReceived(object? sender, string message)
        {
            Logger.Write($"[PROCESSING] Received pipe message: {message}");
            // Handle messages from other modules if needed
        }

        private async Task RunWebSocket(CancellationToken cancellationToken)
        {
            var backoffMs = 500;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    _webSocket = ws;
                    
                    Logger.Write($"[PROCESSING] Connecting to WebSocket at {_wsUri}");
                    Logger.Gui("Attempting to reconnect to Tikfinity");
                    await ws.ConnectAsync(_wsUri, cancellationToken);
                    Logger.Write("[PROCESSING] WebSocket connected successfully");
                    Logger.Gui("Connected to TikFinity");
                    
                    backoffMs = 500; // Reset backoff on successful connection
                    
                    var buffer = new byte[32 * 1024];
                    while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult result;
                        
                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;
                            
                            if (result.Count > 0)
                                ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        
                        var message = Encoding.UTF8.GetString(ms.ToArray());
                        Logger.Write($"[PROCESSING] Received WebSocket message: {Truncate(message, 300)}");
                        
                        // Parse and handle chat message
                        try
                        {
                            using var doc = JsonDocument.Parse(message);
                            if (TryExtractChatData(doc.RootElement, out var chatData))
                            {
                                HandleChatMessage(chatData);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Write($"Error parsing WebSocket message: {ex.Message}", "ERROR");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write($"WebSocket error: {ex.Message}", "ERROR");
                }
                finally
                {
                    _webSocket = null;
                }
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(backoffMs, cancellationToken);
                    backoffMs = Math.Min(backoffMs * 2, 10000);
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;
            return s.Substring(0, max) + "...";
        }

        private static bool TryExtractChatData(JsonElement root, out JsonElement data)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("event", out var ev) && ev.GetString() == "chat" && root.TryGetProperty("data", out var d))
                {
                    data = d;
                    return true;
                }
                
                // Fallback: check if typical chat fields exist at root
                if (root.TryGetProperty("comment", out _))
                {
                    data = root;
                    return true;
                }
            }
            
            data = default;
            return false;
        }

        private void HandleChatMessage(JsonElement data)
        {
            if (_settings == null) return;
            
            // Reload settings if files changed
            _settings.ReloadIfChanged();
            
            // Extract fields
            var nickname = data.TryGetProperty("nickname", out var n) ? (n.GetString() ?? "")
                : (data.TryGetProperty("uniqueId", out var uid) ? (uid.GetString() ?? "") : "");
            var rawComment = data.TryGetProperty("comment", out var c) ? (c.GetString() ?? "") : "";
            var isModerator = ReadBool(data, "isModerator");
            var isSubscriber = ReadBool(data, "isSubscriber");
            var topGifterRank = data.TryGetProperty("topGifterRank", out var t) ? t.GetRawText() : null;
            var followRole = data.TryGetProperty("followRole", out var f) ? f.GetInt32() : 0;

            // Log user info and comment to GUI only (no timestamps)
            var followStatusDisplay = followRole == 0 ? "NO" : (followRole == 1 ? "YES" : (followRole == 2 ? "FRIEND" : "UNKNOWN"));
            var userInfo = $"{nickname} | Subscriber: {isSubscriber} | Moderator: {isModerator} | Top Gifter: {topGifterRank} | Follower: {followStatusDisplay}";
            Logger.Gui(userInfo);
            Logger.Gui(rawComment);

            // Handle restart command first (special case - clears audio queue)
            if (rawComment.StartsWith("!restart", StringComparison.OrdinalIgnoreCase))
            {
                if (isModerator && _emergencyStopActive)
                {
                    // Clear the audio queue to stop flooding
                    if (_audioQueueModule != null)
                    {
                        _audioQueueModule.ClearQueue();
                        Logger.Write($"!restart command by moderator {nickname} - Audio queue cleared");
                        Logger.Gui($"🔄 Audio queue cleared by {nickname}");
                    }
                }
                else if (!isModerator)
                {
                    Logger.Write($"!restart command ignored - {nickname} is not a moderator");
                }
                else if (!_emergencyStopActive)
                {
                    Logger.Write($"!restart command ignored - Emergency stop is not active");
                }
                return;
            }

            // Block all other command messages from reaching TTS. If applicable and permitted, handle them; otherwise just skip.
            if (IsVoiceCommand(rawComment))
            {
                var handled = CommandHandlers.TryHandle(_settings, nickname, rawComment, isModerator);
                // Reload from disk after any potential write to keep memory in sync
                _settings.ForceReloadFromDisk();
                Logger.Write(handled ? $"Command handled: {rawComment}" : $"Command ignored (not permitted): {rawComment}", level: "INFO");
                return;
            }

            // Pipeline: filters, role/priority voice, bad words, name swap
            var decision = Pipeline.Process(_settings, nickname, rawComment, isModerator, isSubscriber, topGifterRank, followRole);
            if (!decision.ShouldSpeak)
            {
                if (!string.IsNullOrEmpty(decision.SkipReason)) 
                    Logger.Write($"Skip speak for {nickname}: {decision.SkipReason}", level: "DEBUG");
                return;
            }

            // Enqueue TTS job
            var job = new ProcessingJob
            {
                Nickname = decision.DisplayName,
                Text = decision.TextForTts,
                VoiceName = decision.VoiceName,
                Speed = decision.Speed
            };
            Console.WriteLine($"[ENQ] job voice={job.VoiceName} speed={(job.Speed?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "default")}");
            _jobQueue.Enqueue(job);
            Logger.Write($"[PROC->TTS] Job queued, setting signal. Queue count: {_jobQueue.Count}");
            _jobSignal.Set();
            Logger.Write($"[PROC->TTS] Signal set, job should be picked up by JobWorker");
            Logger.Write($"Queued TTS job for {job.Nickname} — voice={job.VoiceName} speed={(job.Speed?.ToString("0.##") ?? "-")}", level: "INFO");
        }

        private static bool IsVoiceCommand(string comment)
        {
            Logger.Write($"[VOICECOMMAND] IsVoiceCommand called with comment='{comment}' at {DateTime.Now:HH:mm:ss.fff}");
            if (string.IsNullOrWhiteSpace(comment)) 
            {
                Logger.Write($"[VOICECOMMAND] Comment is null/whitespace, returning false");
                return false;
            }
            comment = comment.TrimStart();
            Logger.Write($"[VOICECOMMAND] Trimmed comment: '{comment}'");
            var result = comment.StartsWith("!vadd", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!vremove", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!vchange", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!vname", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!vnoname", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!vrude", StringComparison.OrdinalIgnoreCase)
                || comment.StartsWith("!restart", StringComparison.OrdinalIgnoreCase);
            Logger.Write($"[VOICECOMMAND] Result: {result}");
            return result;
        }

        private static bool ReadBool(JsonElement obj, string prop)
        {
            Logger.Write($"[READBOOL] ReadBool called with prop='{prop}' at {DateTime.Now:HH:mm:ss.fff}");
            if (!obj.TryGetProperty(prop, out var el)) 
            {
                Logger.Write($"[READBOOL] Property '{prop}' not found, returning false");
                return false;
            }
            Logger.Write($"[READBOOL] Property found, ValueKind: {el.ValueKind}");
            var result = el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => el.TryGetInt32(out var i) && i != 0,
                _ => false
            };
            Logger.Write($"[READBOOL] Result: {result}");
            return result;
        }

        private async Task JobWorker(CancellationToken cancellationToken)
        {
            Logger.Write("[PROCESSING] Job worker started");
            var inFlight = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ProcessingJob? dequeuedJob;
                    while (inFlight >= _maxInFlight || !_jobQueue.TryDequeue(out dequeuedJob))
                    {
                        _jobSignal.WaitOne(100);
                        if (cancellationToken.IsCancellationRequested)
                            return;
                    }

                    Interlocked.Increment(ref inFlight);
                    var jobCopy = dequeuedJob;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessJob(jobCopy);
                        }
                        catch (Exception ex)
                        {
                            Logger.Write($"Error processing job: {ex.Message}", "ERROR");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref inFlight);
                        }
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Write($"Error in job worker: {ex.Message}", "ERROR");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

              private async Task ProcessJob(ProcessingJob job)
              {
                  Logger.Write($"[PROCESSING] Processing job for {job.Nickname}: {job.Text}");
                  
                  // Send voice selection info to GUI (matching Python script format)
                  var speedText = job.Speed?.ToString("F1") ?? "default";
                  Logger.Gui($"{job.Nickname} will use voice {job.VoiceName} at speed {speedText}");
                  Logger.Gui(""); // Add gap after voice selection
                  
                  // Create TTS request
                  var request = new TtsRequest
                  {
                      Voice = job.VoiceName,
                      Text = $"{job.Nickname} says {job.Text}",
                      Speed = job.Speed
                  };

                  // Call TTS module directly (since it's a DLL in the same process)
                  try
                      {
                          if (_ttsModule != null)
                          {
                              await _ttsModule.ProcessTtsRequest(request);
                              Logger.Write($"[PROCESSING] TTS request processed for {job.Nickname}");
                          }
                          else
                          {
                              Logger.Write("[PROCESSING] TTS module not initialized", "ERROR");
                          }
                      }
                      catch (Exception ex)
                      {
                          Logger.Write($"Error processing TTS request: {ex.Message}", "ERROR");
                      }
              }
    }
}
