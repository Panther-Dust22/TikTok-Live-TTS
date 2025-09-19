using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TTS.Shared.Interfaces;

namespace TTS.Shared.Services
{
    public class NamedPipeService : INamedPipeService, IDisposable
    {
        private NamedPipeServerStream? _server;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed = false;

        public event EventHandler<string>? MessageReceived;

        public Task StartServerAsync(string pipeName)
        {
            if (_server != null)
                throw new InvalidOperationException("Server is already running");

            _cancellationTokenSource = new CancellationTokenSource();
            _server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            
            // Start listening for connections
            _ = Task.Run(async () => await ListenForConnectionsAsync(_cancellationTokenSource.Token));
            return Task.CompletedTask;
        }

        private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _server != null)
            {
                try
                {
                    await _server.WaitForConnectionAsync(cancellationToken);
                    
                    _reader = new StreamReader(_server, Encoding.UTF8);
                    _writer = new StreamWriter(_server, Encoding.UTF8) { AutoFlush = true };

                    // Start listening for messages
                    _ = Task.Run(async () => await ListenForMessagesAsync(cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in ListenForConnectionsAsync: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                try
                {
                    var message = await _reader.ReadLineAsync();
                    if (message != null)
                    {
                        MessageReceived?.Invoke(this, message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in ListenForMessagesAsync: {ex.Message}");
                    break;
                }
            }
        }

        public async Task SendMessageAsync(string pipeName, string message)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(5000);
                
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                await writer.WriteLineAsync(message);
            }
            catch (TimeoutException)
            {
                Utils.Logger.Write($"Timeout connecting to pipe '{pipeName}' - server may not be running", "ERROR");
                throw;
            }
            catch (Exception ex)
            {
                Utils.Logger.Write($"Error sending message to pipe '{pipeName}': {ex.Message}", "ERROR");
                throw;
            }
        }

        public async Task<string> ReceiveMessageAsync()
        {
            if (_reader == null)
                throw new InvalidOperationException("Server is not connected");

            return await _reader.ReadLineAsync() ?? "";
        }

        public Task StopServerAsync()
        {
            _cancellationTokenSource?.Cancel();
            
            _writer?.Close();
            _reader?.Close();
            _server?.Close();
            
            _writer?.Dispose();
            _reader?.Dispose();
            _server?.Dispose();
            
            _writer = null;
            _reader = null;
            _server = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopServerAsync().Wait();
                _disposed = true;
            }
        }
    }
}
