using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TTS.Shared.Models;
using TTS.Shared.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TTS.Shared.Services
{
    public class AudioPlayerService : IDisposable
    {
        private readonly ConcurrentQueue<AudioItem> _playbackQueue = new();
        private readonly AutoResetEvent _newItemSignal = new(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _playbackTask;
        private bool _isPlaying = false;
        private string? _ffmpegPath;

        public AudioPlayerService()
        {
            _ffmpegPath = FindFfmpeg();
            if (_ffmpegPath == null)
            {
                Logger.Write("ffmpeg.exe not found - speed control will be limited", "WARN");
            }
            
            
            // Start the playback worker task
            _playbackTask = Task.Run(PlaybackWorker);
            Logger.Write("[AUDIOPLAYER] AudioPlayerService started - ready to process queue");
        }

        private string? FindFfmpeg()
        {
            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(exeDir, "ffmpeg.exe");
                if (File.Exists(local)) return local;
                
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        // Add audio item to the playback queue
        public void EnqueueAudio(AudioItem audioItem)
        {
            _playbackQueue.Enqueue(audioItem);
            _newItemSignal.Set(); // Signal that a new item is available
            Logger.Write($"[AUDIOPLAYER] Enqueued audio item. Queue size: {_playbackQueue.Count}");
        }

        // Background worker that continuously processes the queue
        private async Task PlaybackWorker()
        {
            Logger.Write("[AUDIOPLAYER] Playback worker started");
            
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_playbackQueue.TryDequeue(out var audioItem))
                    {
                        Logger.Write($"[AUDIOPLAYER] Processing audio item from queue. Remaining: {_playbackQueue.Count}");
                        await PlayAudioItem(audioItem);
                    }
                    else
                    {
                        // Wait for a new item or cancellation
                        await Task.Run(() => _newItemSignal.WaitOne(1000), _cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[AUDIOPLAYER] Error in playback worker: {ex.Message}", "ERROR");
                }
            }
            
            Logger.Write("[AUDIOPLAYER] Playback worker stopped");
        }

        // Play a single audio item (this is where the actual playback happens)
        private async Task PlayAudioItem(AudioItem audioItem)
        {
            try
            {
                _isPlaying = true;
                Logger.Write($"[AUDIOPLAYER] Playing: {audioItem.Text} (speed={audioItem.Speed:0.##})");
                // Don't log playing messages to GUI to reduce noise

                // If speed is 1.0 and volume is 1.0, play directly
                if (Math.Abs(audioItem.Speed - 1.0f) < 0.01f && Math.Abs(audioItem.Volume - 1.0f) < 0.01f)
                {
                    await PlayAudioDirectly(audioItem);
                    return;
                }

                // For speed/volume changes, use ffmpeg
                if (_ffmpegPath == null)
                {
                    Logger.Write("ffmpeg not available for speed/volume control. Playing directly.", "WARN");
                    await PlayAudioDirectly(audioItem);
                    return;
                }

                var tempFile = Path.GetTempFileName() + ".wav";
                var inputFilePath = Path.GetTempFileName() + ".mp3";
                await File.WriteAllBytesAsync(inputFilePath, audioItem.Data);

                // Build ffmpeg command
                var arguments = $"-i \"{inputFilePath}\" -af \"atempo={audioItem.Speed},volume={audioItem.Volume}\" -ar 44100 -ac 1 -f wav \"{tempFile}\"";

                Logger.Write($"[AUDIOPLAYER] Creating speed/volume adjusted audio with ffmpeg");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode != 0 || !File.Exists(tempFile))
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Logger.Write($"ffmpeg error: {error}", "ERROR");
                    await PlayAudioDirectly(audioItem);
                }
                else
                {
                    // Play the speed-adjusted file
                    var adjustedAudioItem = new AudioItem
                    {
                        Data = await File.ReadAllBytesAsync(tempFile),
                        SampleRate = 44100,
                        Channels = 1,
                        Speed = 1.0f,
                        Volume = 1.0f,
                        Text = audioItem.Text,
                        Voice = audioItem.Voice
                    };
                    await PlayAudioDirectly(adjustedAudioItem);
                }

                // Clean up temp files
                try { File.Delete(inputFilePath); } catch { }
                try { File.Delete(tempFile); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Write($"Error playing audio: {ex.Message}", "ERROR");
            }
            finally
            {
                _isPlaying = false;
                Logger.Write($"[AUDIOPLAYER] Finished playing: {audioItem.Text}");
            }
        }

        private async Task PlayAudioDirectly(AudioItem audioItem)
        {
            try
            {
                Logger.Write($"[AUDIOPLAYER] Playing audio with NAudio: {audioItem.Text}");

                // Create a temporary file for the audio data
                var tempFile = Path.GetTempFileName() + ".mp3";
                await File.WriteAllBytesAsync(tempFile, audioItem.Data);

                try
                {
                    // Use AudioFileReader for audio playback
                    using var audioFile = new AudioFileReader(tempFile);
                    
                    // Normalize to mono and 44.1kHz if needed
                    ISampleProvider sampleProvider = audioFile;
                    if (audioFile.WaveFormat.Channels == 2)
                        sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
                    if (audioFile.WaveFormat.SampleRate != 44100)
                        sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 44100);

                    // Play audio
                    using var waveOut = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 3 };
                    waveOut.Init(sampleProvider);
                    
                    // Start playback
                    waveOut.Play();
                    Logger.Write($"[AUDIOPLAYER] Started playback for: {audioItem.Text}");

                    // Wait for playback to complete
                    while (waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        await Task.Delay(100);
                    }

                    Logger.Write($"[AUDIOPLAYER] Audio playback completed: {audioItem.Text}");
                }
                finally
                {
                    // Clean up temp file
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"Error playing audio with NAudio: {ex.Message}", "ERROR");
            }
        }

        public bool IsPlaying => _isPlaying;
        public int QueueCount => _playbackQueue.Count;

        // Clear the audio queue (for !restart command)
        public void ClearQueue()
        {
            var clearedCount = 0;
            while (_playbackQueue.TryDequeue(out _))
            {
                clearedCount++;
            }
            Logger.Write($"[AUDIOPLAYER] Cleared {clearedCount} items from audio queue");
        }


        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _newItemSignal?.Set(); // Release any waiting threads
            _playbackTask?.Wait(5000); // Wait up to 5 seconds for graceful shutdown
            _cancellationTokenSource?.Dispose();
            _newItemSignal?.Dispose();
            Logger.Write("[AUDIOPLAYER] AudioPlayerService disposed");
        }
    }
}