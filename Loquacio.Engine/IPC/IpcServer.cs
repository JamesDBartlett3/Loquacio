using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Loquacio.Engine.Services;
using Loquacio.Infrastructure;
using Loquacio.Ipc;
using Loquacio.Services;

namespace Loquacio.Engine.Ipc;

/// <summary>
/// IPC server that accepts controller connections over Unix domain sockets (Linux/macOS)
/// or named pipes (Windows). Newline-delimited JSON protocol.
/// </summary>
public class IpcServer : IAsyncDisposable
{
    private const string PipeName = "loquacio";

    /// <summary>
    /// Per-command handler timeout. No single IPC command may block a connection
    /// (and therefore wedge the engine pipeline) indefinitely. Model loads can be
    /// slow, so this is generous — but finite. Tests override this.
    /// </summary>
    public static TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _services;
    private readonly ILogger<IpcServer> _logger;
    private readonly string? _socketPathOverride;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<Stream> _clients = [];
    private readonly Dictionary<Stream, SemaphoreSlim> _clientWriteLocks = [];
    private readonly object _clientsLock = new();
    private bool _disposed;
    private Socket? _unixListener;

    /// <summary>
    /// Serializes settings updates so a timed-out (abandoned) handler can't race a
    /// retrying controller into last-writer-wins on the settings cache/file.
    /// </summary>
    private static readonly SemaphoreSlim _settingsUpdateGate = new(1, 1);

    /// <summary>
    /// Human-readable endpoint description for logging.
    /// </summary>
    public string EndpointPath => GetEndpointDescription();

    public IpcServer(IServiceProvider services, ILogger<IpcServer> logger, string? socketPathOverride = null)
    {
        _services = services;
        _logger = logger;
        _socketPathOverride = socketPathOverride;
        _pipeName = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(socketPathOverride)
            ? Path.GetFileName(socketPathOverride)
            : PipeName;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (OperatingSystem.IsWindows())
        {
            _acceptTask = Task.Run(() => NamedPipeAcceptLoopAsync(_cts.Token), _cts.Token);
            _logger.LogInformation("IPC server listening on named pipe \\\\.\\pipe\\{PipeName}", PipeName);
        }
        else
        {
            StartUnixSocket(ct);
        }

        return Task.CompletedTask;
    }

    private void StartUnixSocket(CancellationToken ct)
    {
        var endpointPath = GetUnixSocketPath();
        var dir = Path.GetDirectoryName(endpointPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(endpointPath))
            {
                try { File.Delete(endpointPath); } catch { }
            }
        }

        _unixListener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _unixListener.Bind(new UnixDomainSocketEndPoint(endpointPath));
        _unixListener.Listen(8);

        var token = _cts?.Token ?? ct;
        _acceptTask = Task.Run(() => UnixSocketAcceptLoopAsync(token), token);

        _logger.LogInformation("IPC server listening on {Path}", endpointPath);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _unixListener?.Close();
        _unixListener = null;

        lock (_clientsLock)
        {
            foreach (var c in _clients)
            {
                try { c.Close(); } catch { }
            }
            _clients.Clear();
            foreach (var lockSem in _clientWriteLocks.Values)
            {
                lockSem.Dispose();
            }
            _clientWriteLocks.Clear();
        }

        if (_acceptTask != null)
        {
            try { await _acceptTask; } catch { }
        }

        // Clean up Unix socket file
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var path = GetUnixSocketPath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        _logger.LogInformation("IPC server stopped");
    }

    // ─── Unix Socket Accept Loop ───

    private async Task UnixSocketAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _unixListener != null)
        {
            try
            {
                var socket = await _unixListener.AcceptAsync(ct);
                var stream = new NetworkStream(socket, ownsSocket: true);
                lock (_clientsLock)
                {
                    _clients.Add(stream);
                    _clientWriteLocks[stream] = new SemaphoreSlim(1, 1);
                }
                _ = HandleConnectionAsync(stream, socket.RemoteEndPoint?.ToString() ?? "unknown", ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accepting IPC connection");
            }
        }
    }

    // ─── Named Pipe Accept Loop (Windows) ───

    private async Task NamedPipeAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // Wait for a client to connect
                await pipeServer.WaitForConnectionAsync(ct);

                lock (_clientsLock)
                {
                    _clients.Add(pipeServer);
                    _clientWriteLocks[pipeServer] = new SemaphoreSlim(1, 1);
                }

                _ = HandleConnectionAsync(pipeServer, "named-pipe-client", ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accepting named pipe connection");
                try { pipeServer?.Dispose(); } catch { }
            }
        }
    }

    // ─── Shared Connection Handler ───

    private async Task HandleConnectionAsync(Stream stream, string remoteId, CancellationToken ct)
    {
        _logger.LogDebug("Controller connected: {Remote}", remoteId);

        StreamReader? reader = null;
        StreamWriter? writer = null;

        try
        {
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            // Send initial status snapshot
            await SendMessageAsync(stream, writer, GetCurrentStatus(), ct);

            string? line;
            while (!ct.IsCancellationRequested &&
                   (line = await reader.ReadLineAsync(ct)) != null)
            {
                var message = DeserializeMessage(line);
                if (message == null)
                {
                    await SendMessageAsync(stream, writer, new AckMessage { Success = false, Error = "Invalid message" }, ct);
                    continue;
                }

                var response = await HandleMessageWithTimeoutAsync(message, ct);
                if (response != null)
                {
                    // Propagate correlation ID from request to response
                    // so controllers can match acks to their commands
                    response.CorrelationId = message.CorrelationId;
                    await SendMessageAsync(stream, writer, response, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling IPC client");
        }
        finally
        {
            _logger.LogDebug("Controller disconnected: {Remote}", remoteId);
            lock (_clientsLock)
            {
                _clients.Remove(stream);
                if (_clientWriteLocks.TryGetValue(stream, out var writeLock))
                {
                    _clientWriteLocks.Remove(stream);
                    writeLock.Dispose();
                }
            }
            try { reader?.Dispose(); } catch { }
            try { writer?.Dispose(); } catch { }
            try { stream.Close(); } catch { }
        }
    }

    private async Task<IpcMessage?> HandleMessageWithTimeoutAsync(IpcMessage message, CancellationToken ct)
    {
        // Race the handler against a timeout — an internally-hung handler (deadlock,
        // blocking I/O) can't observe the cancellation token, so we can't just
        // cancel it; we abandon it and answer with a timeout Ack instead.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HandlerTimeout);

        var handlerTask = HandleMessageAsync(message, timeoutCts.Token);
        var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);

        var completed = await Task.WhenAny(handlerTask, timeoutTask);
        if (completed == timeoutTask)
        {
            _logger.LogWarning("IPC command {Type} timed out after {Timeout}s — handler abandoned", message.MessageType, HandlerTimeout.TotalSeconds);
            return new AckMessage { Success = false, Error = $"Command '{message.MessageType}' timed out" };
        }

        return await handlerTask;
    }

    private async Task<IpcMessage?> HandleMessageAsync(IpcMessage message, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        return message switch
        {
            SubscribeMessage => GetCurrentStatus(),
            GetStatusMessage => GetCurrentStatus(),
            StartListeningMessage => await DoToggleListening(sp, start: true),
            StopListeningMessage => await DoToggleListening(sp, start: false),
            SetModeMessage mode => await DoSetMode(sp, mode.Mode, ct),
            GetHistoryMessage hist => await DoGetHistory(sp, hist),
            UpdateSettingsMessage settings => await DoUpdateSettings(sp, settings, ct),
            GetSettingsMessage => DoGetSettings(sp),
            GetDevicesMessage => DoGetDevices(sp),
            CancelDictationMessage => DoCancelDictation(sp),
            ReprocessLastMessage => await DoReprocessLast(sp),
            _ => new AckMessage { Success = false, Error = $"Unknown message type: {message.MessageType}" }
        };
    }

    private StatusUpdateMessage GetCurrentStatus()
    {
        using var scope = _services.CreateScope();
        var am = scope.ServiceProvider.GetRequiredService<IActivationManagerService>();
        // Transcription service is optional here so status still renders in
        // minimal hosts (and test harnesses) that only wire the activation manager.
        var ts = scope.ServiceProvider.GetService<IBackgroundTranscriptionService>();
        var pipelineState = ts?.PipelineState ?? DictationState.Idle;
        var (text, color) = PipelineStatusStyle.Describe(pipelineState, am.IsListening);

        return new StatusUpdateMessage
        {
            IsListening = am.IsListening,
            Mode = am.CurrentMode,
            StatusText = text,
            StatusColor = color,
            PipelineState = pipelineState.ToString(),
            SessionId = ts?.PipelineSessionId?.ToString()
        };
    }

    private static async Task<AckMessage> DoToggleListening(IServiceProvider sp, bool start)
    {
        try
        {
            var am = sp.GetRequiredService<IActivationManagerService>();
            if (start && !am.IsListening)
                await am.ToggleListeningAsync();
            else if (!start && am.IsListening)
                await am.ToggleListeningAsync();
            return new AckMessage { Success = true };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = ex.Message };
        }
    }

    private static AckMessage DoCancelDictation(IServiceProvider sp)
    {
        // Optional resolution: minimal hosts / test harnesses without a transcription
        // service still get a coherent ack instead of a handler crash.
        var ts = sp.GetService<IBackgroundTranscriptionService>();
        if (ts is null)
            return new AckMessage { Success = false, Error = "transcription service unavailable" };

        try
        {
            var wasActive = ts.CancelActiveRun();
            // Error carries human-readable outcome data (same convention as get-history).
            return new AckMessage { Success = true, Error = wasActive ? "cancelled active dictation run" : "no active run" };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = ex.Message };
        }
    }

    private static async Task<AckMessage> DoReprocessLast(IServiceProvider sp)
    {
        // Optional resolution — same convention as cancel-dictation.
        var ts = sp.GetService<IBackgroundTranscriptionService>();
        if (ts is null)
            return new AckMessage { Success = false, Error = "transcription service unavailable" };

        try
        {
            var result = await ts.ReprocessLastAsync();
            if (result is null)
                return new AckMessage { Success = false, Error = "no previous dictation result to reprocess" };

            // Error carries the reprocessed result JSON (same convention as get-history).
            return new AckMessage
            {
                Success = true,
                Error = JsonSerializer.Serialize(result, IpcJson.Options)
            };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = ex.Message };
        }
    }

    private static async Task<AckMessage> DoSetMode(
        IServiceProvider sp, ActivationMode mode, CancellationToken ct)
    {
        try
        {
            var am = sp.GetRequiredService<IActivationManagerService>();
            await am.SwitchModeAsync(mode, ct);
            return new AckMessage { Success = true };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = ex.Message };
        }
    }

    private static async Task<IpcMessage> DoGetHistory(
        IServiceProvider sp, GetHistoryMessage req)
    {
        var hs = sp.GetRequiredService<IHistoryService>();
        var limit = req.Limit ?? 10;
        var entries = await hs.GetRecentHistoryAsync(limit);
        return new AckMessage
        {
            Success = true,
            Error = JsonSerializer.Serialize(entries)
        };
    }

    /// <summary>
    /// Broadcast a message to all connected controllers (async, serialized per-stream).
    /// </summary>
    public async Task BroadcastMessageAsync(IpcMessage message)
    {
            var json = JsonSerializer.Serialize(message, message.GetType(), IpcJson.Options) + "\n";
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        List<Stream> streamsCopy;
        lock (_clientsLock)
        {
            streamsCopy = _clients.ToList();
        }

        var tasks = streamsCopy.Select(async stream =>
        {
            SemaphoreSlim? writeLock;
            lock (_clientsLock)
            {
                _clientWriteLocks.TryGetValue(stream, out writeLock);
            }

            if (writeLock == null) return;

            bool acquired = false;
            try
            {
                await writeLock.WaitAsync();
                acquired = true;
                if (stream.CanWrite)
                {
                    await stream.WriteAsync(data, CancellationToken.None);
                    await stream.FlushAsync(CancellationToken.None);
                }
            }
            catch (ObjectDisposedException)
            {
                // Stream was disposed while we were waiting for the lock
                // This is expected during concurrent disconnect races
            }
            catch { }
            finally
            {
                // Only release if WE acquired — a bare CurrentCount check can
                // over-release when another holder holds the semaphore.
                if (acquired)
                {
                    writeLock.Release();
                }
            }
        });

        await Task.WhenAll(tasks);
    }

    private static IpcMessage DoGetSettings(IServiceProvider sp)
    {
        var ss = sp.GetRequiredService<ISettingsService>();
        return new SettingsSnapshotMessage { Settings = ss.GetSettingsAsync().GetAwaiter().GetResult() };
    }

    private static IpcMessage DoGetDevices(IServiceProvider sp)
    {
        try
        {
            var audio = sp.GetRequiredService<IAudioCaptureService>();
            return new DevicesListMessage { Devices = [.. audio.GetAvailableDevices()] };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = $"Failed to enumerate devices: {ex.Message}" };
        }
    }

    private async Task<AckMessage> DoUpdateSettings(
        IServiceProvider sp, UpdateSettingsMessage req, CancellationToken ct)
    {
        await _settingsUpdateGate.WaitAsync(ct);
        try
        {
            var ss = sp.GetRequiredService<ISettingsService>();
            var previousSettings = await ss.GetSettingsAsync(ct);
            // Deep-copy so a rejected/rolled-back update never mutates the live
            // settings object (GetSettingsAsync returns the cached instance).
            var settings = JsonSerializer.Deserialize<Settings>(
                JsonSerializer.Serialize(previousSettings)) ?? previousSettings;

            // Each entry is a settings section ("audio", "whisper", ...). Values arrive
            // as JsonElement after JSON round-trip. Unknown sections are rejected.
            foreach (var (section, value) in req.Settings)
            {
                var el = value is JsonElement je ? je
                    : JsonSerializer.SerializeToElement(value);

                switch (section.ToLowerInvariant())
                {
                    case "audio": settings.Audio = DeserializeSection<AudioSettings>(el, section); break;
                    case "whisper": settings.Whisper = DeserializeSection<WhisperSettings>(el, section); break;
                    case "llm": settings.LLM = DeserializeSection<LLMSettings>(el, section); break;
                    case "activation": settings.Activation = DeserializeSection<ActivationSettings>(el, section); break;
                    case "vocabulary": settings.Vocabulary = DeserializeSection<VocabularySettings>(el, section); break;
                    case "output": settings.Output = DeserializeSection<OutputSettings>(el, section); break;
                    case "tray": settings.Tray = DeserializeSection<TraySettings>(el, section); break;
                    default:
                        return new AckMessage { Success = false, Error = $"Unknown settings section: '{section}'" };
                }
            }

            // ── Validate BEFORE persisting or touching the pipeline ──
            if (HasSection(req, "audio"))
            {
                var devices = sp.GetRequiredService<IAudioCaptureService>().GetAvailableDevices();
                if (devices.Count > 0
                    && !string.IsNullOrWhiteSpace(settings.Audio.DeviceId)
                    && !string.Equals(settings.Audio.DeviceId, "default", StringComparison.OrdinalIgnoreCase)
                    && !devices.Any(d => string.Equals(d.Id, settings.Audio.DeviceId, StringComparison.OrdinalIgnoreCase)))
                {
                    return new AckMessage
                    {
                        Success = false,
                        Error = $"Unknown Audio.DeviceId '{settings.Audio.DeviceId}'. Available: {string.Join(", ", devices.Select(d => d.Id))}"
                    };
                }
            }

            if (HasSection(req, "whisper")
                && !string.IsNullOrWhiteSpace(settings.Whisper.ModelPath)
                && !File.Exists(settings.Whisper.ModelPath))
            {
                return new AckMessage
                {
                    Success = false,
                    Error = $"Whisper.ModelPath '{settings.Whisper.ModelPath}' does not exist"
                };
            }

            if (HasSection(req, "output")
                && !new[] { "inject", "type", "clipboard" }.Contains(settings.Output.Mode, StringComparer.OrdinalIgnoreCase))
            {
                return new AckMessage
                {
                    Success = false,
                    Error = $"Unknown Output.Mode '{settings.Output.Mode}'. Allowed: inject, type, clipboard"
                };
            }

            // Apply changes live so settings actually take effect in the engine.
            var audioChanged = HasSection(req, "audio");
            var whisperChanged = HasSection(req, "whisper");
            var activationChanged = HasSection(req, "activation");

            if (activationChanged)
            {
                // Re-register global hotkeys from the new Activation.Hotkey
                try { await sp.GetRequiredService<HotkeyManager>().ReapplyHotkeysAsync(ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to reapply hotkeys after settings update"); }

                // Live mode change: persisting Activation.Mode is not enough — the
                // activation manager has to switch so the new mode actually takes
                // effect (and a mode status update is broadcast to all controllers).
                if (!string.Equals(settings.Activation.Mode, previousSettings.Activation.Mode, StringComparison.OrdinalIgnoreCase))
                {
                    var mode = settings.Activation.Mode.ToLowerInvariant() switch
                    {
                        "push-to-talk" => ActivationMode.PushToTalk,
                        _ => ActivationMode.Continuous,
                    };
                    try { await sp.GetRequiredService<IActivationManagerService>().SwitchModeAsync(mode, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to apply activation mode change"); }
                }

                // Restart keyword detection with the new keyword
                try { await sp.GetRequiredService<IActivationManagerService>().ApplyActivationSettingsAsync(ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to reapply activation settings"); }
            }

            if ((audioChanged || whisperChanged) && sp.GetRequiredService<IActivationManagerService>() is { IsListening: true } am)
            {
                // Restart the pipeline so the new device/model takes effect immediately.
                // Save the new settings FIRST (StartAsync re-reads them), restart, and
                // if the start toggle fails, roll back so the engine is never left
                // wedged not-listening: restore the previous settings and restart again.
                await ss.SaveSettingsAsync(settings, ct);
                await am.ToggleListeningAsync(ct); // stop
                try
                {
                    await am.ToggleListeningAsync(ct);
                }
                catch (Exception startEx)
                {
                    _logger.LogError(startEx, "Pipeline restart failed after settings update — rolling back to previous settings");
                    await ss.SaveSettingsAsync(previousSettings, ct);
                    try { await am.ToggleListeningAsync(ct); } // restart with old settings
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "Rollback restart also failed — engine left stopped; recoverable via start-listening");
                    }
                    return new AckMessage
                    {
                        Success = false,
                        Error = $"Pipeline failed to start with the new settings ({startEx.Message}); previous settings restored and pipeline restarted"
                    };
                }
            }
            else
            {
                await ss.SaveSettingsAsync(settings, ct);
            }

            await BroadcastMessageAsync(new SettingsChangedMessage());
            return new AckMessage { Success = true };
        }
        catch (Exception ex)
        {
            return new AckMessage { Success = false, Error = $"Settings update failed: {ex.Message}" };
        }
        finally
        {
            _settingsUpdateGate.Release();
        }
    }

    private static bool HasSection(UpdateSettingsMessage req, string name) =>
        req.Settings.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static T DeserializeSection<T>(JsonElement el, string section) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(el.GetRawText())
                ?? throw new JsonException($"Section '{section}' deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Invalid '{section}' settings section: {ex.Message}", ex);
        }
    }

    private async Task SendMessageAsync(Stream stream, StreamWriter writer, IpcMessage message, CancellationToken ct)
    {
        SemaphoreSlim? writeLock;
        lock (_clientsLock)
        {
            _clientWriteLocks.TryGetValue(stream, out writeLock);
        }

        if (writeLock != null)
        {
            bool acquired = false;
            try
            {
                await writeLock.WaitAsync(ct);
                acquired = true;
                var json = JsonSerializer.Serialize(message, message.GetType(), IpcJson.Options);
                await writer.WriteLineAsync(json);
            }
            catch (ObjectDisposedException)
            {
                // Stream was disposed while we were waiting for the lock
                // This is expected during concurrent disconnect races
            }
            finally
            {
                if (acquired)
                {
                    writeLock.Release();
                }
            }
        }
        else
        {
            // Fallback: stream already removed from client list, best-effort write
            var json = JsonSerializer.Serialize(message, message.GetType());
            await writer.WriteLineAsync(json);
        }
    }

    private static IpcMessage? DeserializeMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var type = doc.RootElement.GetProperty("type").GetString();
            return type switch
            {
                "subscribe" => JsonSerializer.Deserialize<SubscribeMessage>(line, IpcJson.Options),
                "get-status" => JsonSerializer.Deserialize<GetStatusMessage>(line, IpcJson.Options),
                "start-listening" => JsonSerializer.Deserialize<StartListeningMessage>(line, IpcJson.Options),
                "stop-listening" => JsonSerializer.Deserialize<StopListeningMessage>(line, IpcJson.Options),
                "set-mode" => JsonSerializer.Deserialize<SetModeMessage>(line, IpcJson.Options),
                "get-history" => JsonSerializer.Deserialize<GetHistoryMessage>(line, IpcJson.Options),
                "update-settings" => JsonSerializer.Deserialize<UpdateSettingsMessage>(line, IpcJson.Options),
                "get-settings" => JsonSerializer.Deserialize<GetSettingsMessage>(line, IpcJson.Options),
                "get-devices" => JsonSerializer.Deserialize<GetDevicesMessage>(line, IpcJson.Options),
                "cancel-dictation" => JsonSerializer.Deserialize<CancelDictationMessage>(line, IpcJson.Options),
                "reprocess-last" => JsonSerializer.Deserialize<ReprocessLastMessage>(line, IpcJson.Options),
                _ => null
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Endpoint path for logging. Unix socket path on Linux/macOS, pipe name on Windows.
    /// </summary>
    private string GetEndpointDescription()
    {
        if (OperatingSystem.IsWindows())
            return $@"\\.\pipe\{_pipeName}";

        return GetUnixSocketPath();
    }

    private string GetUnixSocketPath()
    {
        if (_socketPathOverride != null)
            return _socketPathOverride;

        return GetDefaultUnixSocketPath();
    }

    private static string GetDefaultUnixSocketPath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "loquacio.sock");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "loquacio", "engine.sock");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
    }
}
