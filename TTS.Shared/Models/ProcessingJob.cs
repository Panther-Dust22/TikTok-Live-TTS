using System.Text.Json;

namespace TTS.Shared.Models
{
    public class ProcessingJob
    {
        public string Nickname { get; set; } = "";
        public string Text { get; set; } = "";
        public string VoiceName { get; set; } = "";
        public float? Speed { get; set; }
        
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            });
        }
        
        public static ProcessingJob? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessingJob>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
