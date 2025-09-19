using System;
using System.Threading.Tasks;

namespace TTS.Shared.Interfaces
{
    public interface INamedPipeService : IDisposable
    {
        Task StartServerAsync(string pipeName);
        Task StopServerAsync();
        Task SendMessageAsync(string pipeName, string message);
        Task<string> ReceiveMessageAsync();
        event EventHandler<string> MessageReceived;
    }
}
