using System;
using System.Threading.Tasks;
using TTS.Shared.Interfaces;
using TTS.Shared.Models;
using TTS.Shared.Services;
using TTS.Shared.Utils;

namespace TTS.AudioQueue
{
    public class AudioQueueModule : IModule
    {
        public string Name => "AudioQueue";
        public bool IsRunning { get; private set; }

        private AudioPlayerService? _audioPlayerService;

        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            Logger.Write("[AUDIOQUEUE] Starting AudioQueue module");
            
            // Initialize the audio player service (it manages its own queue and playback)
            _audioPlayerService = new AudioPlayerService();
            
            IsRunning = true;
            Logger.Write("[AUDIOQUEUE] AudioQueue module started successfully");
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
                return;

            Logger.Write("[AUDIOQUEUE] Stopping AudioQueue module");
            
            // Dispose the audio player service (stops playback and cleans up)
            _audioPlayerService?.Dispose();
            _audioPlayerService = null;
            
            IsRunning = false;
            Logger.Write("[AUDIOQUEUE] AudioQueue module stopped");
        }

        // Public method for other modules to enqueue audio
        public void EnqueueAudio(AudioItem audioItem)
        {
            _audioPlayerService?.EnqueueAudio(audioItem);
        }


        // Public method to get current queue count
        public int GetQueueCount()
        {
            return _audioPlayerService?.QueueCount ?? 0;
        }

        // Public method to clear the audio queue (for !restart command)
        public void ClearQueue()
        {
            _audioPlayerService?.ClearQueue();
            Logger.Write("[AUDIOQUEUE] Audio queue cleared by !restart command");
        }
    }
}
