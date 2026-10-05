using System.Net.Sockets;
using System.IO.Pipes;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Ipc;

/// <summary>
/// Concrete IPC client for controllers to communicate with the daemon.
/// Uses Unix domain sockets on Linux/macOS, named pipes on Windows.
/// Newline-delimited JSON protocol matching <see cref="IpcServer"/>.
/// </summary>
public sealed class DaemonProxy : IDaemonProxy
{
    private const string PipeName = "whisper-dictation";

    private readonly ILogger<DaemonProxy>? _logger;
    private readonly string? _socketPathOverride;
    private readonly string? _pipeNameOverride;
    private Socket? _socket;
    private NamedPipeClientStream? _pipeStream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private Action<IpcMessage>? _onMessage;
    private bool _disposed;

    // Pending command response waiters
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcMessage>> _pending = new();
    private int _correlationCounter;

    // Serializes writes so concurrent sends from different threads can't interleave lines
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => (_socket?.Connected == true) || (_pipeStream?.IsConnected == true);

    public event EventHandler? Disconnected;

    public DaemonProxy(ILogger<DaemonProxy>? logger = null, string? socketPathOverride = null)
    {
        _logger = logger;
        _socketPathOverride = socketPathOverride;
        _pipeNameOverride = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(socketPathOverride)
            ? Path.GetFileName(socketPathOverride)
            : null;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        Stream stream;

        if (OperatingSystem.IsWindows())
        {
            stream = await ConnectNamedPipeAsync(cts.Token);
        }
        else
        {
            stream = ConnectUnixSocket(cts.Token);
        }

        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);

        _logger?.LogInformation("Connected to daemon");
    }

    /// <summary>
    /// Connect via Windows named pipe.
    /// </summary>
    private async Task<Stream> ConnectNamedPipeAsync(CancellationToken ct)
    {
        _pipeStream = new NamedPipeClientStream(
            ".",
            _pipeNameOverride ?? PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await _pipeStream.ConnectAsync(ct);

        _logger?.LogInformation("Connected to daemon via named pipe '{PipeName}'", PipeName);
        return _pipeStream;
    }

    /// <summary>
    /// Connect via Unix domain socket (Linux/macOS).
    /// </summary>
    private Stream ConnectUnixSocket(CancellationToken ct)
    {
        var path = _socketPathOverride ?? GetSocketPath();
        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _socket.Connect(new UnixDomainSocketEndPoint(path));

        _logger?.LogInformation("Connected to daemon at {Path}", path);
        return new NetworkStream(_socket, ownsSocket: true);
    }

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();

        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _socket?.Close(); } catch { }
        try { _pipeStream?.Close(); } catch { }
        _writer = null;
        _reader = null;
        _socket = null;
        _pipeStream = null;

        if (_readTask != null)
        {
            try { await _readTask; } catch { }
        }
        _readTask = null;
    }

    public Task<AckMessage> SendCommandAsync(IpcMessage command, CancellationToken ct = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Not connected to daemon");

        return SendAndAwaitAsync(command, ct);
    }

    /// <summary>
    /// Request the daemon's current settings snapshot.
    /// </summary>
    public Task<Settings?> GetSettingsAsync(CancellationToken ct = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Not connected to daemon");

        return AwaitTypedAsync<SettingsSnapshotMessage, Settings?>(new GetSettingsMessage(), m => m.Settings, ct);
    }

    /// <summary>
    /// Request the audio capture devices available to the daemon.
    /// </summary>
    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Not connected to daemon");

        return AwaitTypedAsync<DevicesListMessage, IReadOnlyList<AudioDevice>>(new GetDevicesMessage(), m => m.Devices, ct);
    }

    /// <summary>
    /// Push updated settings sections to the daemon (applied live).
    /// </summary>
    public Task<AckMessage> UpdateSettingsAsync(Settings settings, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["audio"] = settings.Audio,
            ["whisper"] = settings.Whisper,
            ["llm"] = settings.LLM,
            ["activation"] = settings.Activation,
            ["vocabulary"] = settings.Vocabulary,
            ["output"] = settings.Output,
            ["tray"] = settings.Tray,
        };
        return SendAndAwaitAsync(new UpdateSettingsMessage { Settings = payload }, ct);
    }

    /// <summary>
    /// Push one settings section without allowing an unrelated stale section to
    /// cause validation of the entire settings update to fail.
    /// </summary>
    public Task<AckMessage> UpdateSettingsSectionAsync(
        string section, object value, CancellationToken ct = default)
    {
        return SendAndAwaitAsync(new UpdateSettingsMessage
        {
            Settings = new Dictionary<string, object> { [section] = value }
        }, ct);
    }

    /// <summary>
    /// Cancel the currently active dictation run (no-op success when idle).
    /// </summary>
    public Task<AckMessage> CancelDictationAsync(CancellationToken ct = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Not connected to daemon");

        return SendAndAwaitAsync(new CancelDictationMessage(), ct);
    }

    /// <summary>
    /// Re-run post-processing + output for the last completed dictation run
    /// with current settings (no re-transcription).
    /// </summary>
    public Task<AckMessage> ReprocessLastAsync(CancellationToken ct = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Not connected to daemon");

        return SendAndAwaitAsync(new ReprocessLastMessage(), ct);
    }

    private async Task<AckMessage> SendAndAwaitAsync(IpcMessage command, CancellationToken ct)
    {
        // Assign a correlation ID and register a waiter
        command.CorrelationId ??= Interlocked.Increment(ref _correlationCounter).ToString();
        var tcs = new TaskCompletionSource<IpcMessage>();
        _pending[command.CorrelationId] = tcs;

        // Register the timeout/cancellation BEFORE writing: if WriteLine throws
        // (dying socket), the waiter is cleaned up instead of leaking until disconnect.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
        linkedCts.Token.Register(() =>
        {
            _pending.TryRemove(command.CorrelationId, out _);
            tcs.TrySetCanceled(linkedCts.Token);
        });

        try
        {
            await WriteCommandAsync(command);
        }
        catch
        {
            _pending.TryRemove(command.CorrelationId, out _);
            tcs.TrySetCanceled();
            throw;
        }

        // If the command doesn't expect an ack (e.g. subscribe), return immediately
        if (command is SubscribeMessage)
        {
            _pending.TryRemove(command.CorrelationId, out _);
            return new AckMessage { Success = true };
        }

        var response = await tcs.Task.ConfigureAwait(false);
        return response as AckMessage ?? new AckMessage
        {
            Success = true,
            CorrelationId = response.CorrelationId,
            Error = JsonSerializer.Serialize(response, response.GetType(), IpcJson.Options)
        };
    }

    private async Task<TResult> AwaitTypedAsync<TMessage, TResult>(IpcMessage command, Func<TMessage, TResult> select, CancellationToken ct)
        where TMessage : IpcMessage
    {
        command.CorrelationId = Interlocked.Increment(ref _correlationCounter).ToString();
        var tcs = new TaskCompletionSource<IpcMessage>();
        _pending[command.CorrelationId] = tcs;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
        linkedCts.Token.Register(() =>
        {
            _pending.TryRemove(command.CorrelationId, out _);
            tcs.TrySetCanceled(linkedCts.Token);
        });

        try
        {
            await WriteCommandAsync(command);
        }
        catch
        {
            _pending.TryRemove(command.CorrelationId, out _);
            tcs.TrySetCanceled();
            throw;
        }

        var response = await tcs.Task.ConfigureAwait(false);
        return response is TMessage typed ? select(typed) : throw new IOException($"Unexpected response: {response.MessageType}");
    }

    private async Task WriteCommandAsync(IpcMessage command)
    {
        var json = JsonSerializer.Serialize(command, command.GetType(), IpcJson.Options);
        await _sendLock.WaitAsync();
        try { _writer!.WriteLine(json); }
        finally { _sendLock.Release(); }
    }

    public void SubscribeToUpdates(Action<IpcMessage> onMessage)
    {
        _onMessage = onMessage;
    }

    public void UnsubscribeFromUpdates()
    {
        _onMessage = null;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _reader != null)
        {
            try
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                {
                    OnDisconnected();
                    break;
                }

                var msg = DeserializeMessage(line);
                if (msg == null) continue;

                // Route responses to pending command waiters (any message with matching correlation ID)
                if (msg.CorrelationId != null)
                {
                    if (_pending.TryRemove(msg.CorrelationId, out var tcs))
                    {
                        tcs.TrySetResult(msg);
                        continue;
                    }
                }

                // Route to subscriber
                _onMessage?.Invoke(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException)
            {
                OnDisconnected();
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error reading from daemon IPC");
            }
        }
    }

    private void OnDisconnected()
    {
        _logger?.LogWarning("Disconnected from daemon");

        // Fail all pending commands
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(new IOException("Disconnected from daemon"));
        }
        _pending.Clear();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private static IpcMessage? DeserializeMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var type = doc.RootElement.GetProperty("type").GetString();
            return type switch
            {
                "status" => JsonSerializer.Deserialize<StatusUpdateMessage>(line, IpcJson.Options),
                "transcription" => JsonSerializer.Deserialize<TranscriptionResultMessage>(line, IpcJson.Options),
                "ack" => JsonSerializer.Deserialize<AckMessage>(line, IpcJson.Options),
                "settings-changed" => JsonSerializer.Deserialize<SettingsChangedMessage>(line, IpcJson.Options),
                "settings-snapshot" => JsonSerializer.Deserialize<SettingsSnapshotMessage>(line, IpcJson.Options),
                "devices" => JsonSerializer.Deserialize<DevicesListMessage>(line, IpcJson.Options),
                _ => null
            };
        }
        catch { return null; }
    }

    private static string GetSocketPath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "whisper-dictation.sock");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "whisper-dictation", "daemon.sock");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readCts?.Cancel();
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _socket?.Close(); } catch { }
        try { _pipeStream?.Dispose(); } catch { }
        GC.SuppressFinalize(this);
    }
}
