using System.Text.Json;

namespace TTS.Shared.Models
{
    public class TtsRequest
    {
        public string Voice { get; set; } = "";
        public string Text { get; set; } = "";
        public float? Speed { get; set; }
        
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            });
        }
        
        public static TtsRequest? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<TtsRequest>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
