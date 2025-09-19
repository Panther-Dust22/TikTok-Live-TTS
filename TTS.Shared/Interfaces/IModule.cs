using System;
using System.Threading.Tasks;

namespace TTS.Shared.Interfaces
{
    public interface IModule
    {
        string Name { get; }
        Task StartAsync();
        Task StopAsync();
        bool IsRunning { get; }
    }
}
