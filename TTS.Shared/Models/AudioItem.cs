using System;

namespace TTS.Shared.Models
{
    public class AudioItem
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 1;
        public float Speed { get; set; } = 1.0f;
        public float Volume { get; set; } = 1.0f;
        public string Text { get; set; } = "";
        public string Voice { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
