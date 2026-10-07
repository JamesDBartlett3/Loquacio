using System.Reflection;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Text.Json;

namespace Loquacio.Tui;

/// <summary>
/// Lightweight TUI controller for the Loquacio engine.
/// Connects via IPC, shows status and transcription history.
/// Uses plain console output (no Terminal.Gui dependency for initial version).
/// </summary>
public static class Program
{
    private static bool _running = true;
    private static bool _isListening;
    private static ActivationMode _mode = ActivationMode.Continuous;
    private static readonly List<(string text, DateTimeOffset time)> _history = [];
    private static string _statusText = "Connecting…";
    private static string _pipelineState = "?";

    public static async Task Main(string[] args)
    {
        if (HandleInfoArgs(args))
            return;

        // CLI mode: one-shot commands
        if (args.Length > 0)
        {
            await RunCliCommand(args);
            return;
        }

        // Interactive TUI mode
        await RunTui();
    }

    // ── --help / --version ──

    private static bool HandleInfoArgs(string[] args)
    {
        if (args.Length == 0) return false;
        switch (args[0].ToLowerInvariant())
        {
            case "-h":
            case "--help":
            case "help":
                Console.WriteLine("""
                Loquacio TUI controller

                Usage: loquacio-tui [command]

                With no command, starts the interactive TUI (connects to the engine
                via IPC; start the engine first if it is not running).

                Commands:
                  status      One-shot: print engine status (listening, mode, pipeline state)
                  start       One-shot: start listening
                  stop        One-shot: stop listening
                  cancel      One-shot: cancel the active dictation run
                  reprocess   One-shot: re-run post-processing on the last transcription
                  history     One-shot: print recent transcription history (JSON)
                  help        Show this help and exit
                  version     Show version information and exit

                Interactive keys: F1 start/stop · F2 cycle mode · F3 cancel · F4 history · Ctrl+C quit
                """);
                return true;

            case "-v":
            case "--version":
            case "version":
                Console.WriteLine($"loquacio-tui {GetVersion()}");
                return true;

            default:
                return false;
        }
    }

    private static string GetVersion()
    {
        var attr = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    // ── CLI Controller (bonus quick win) ──

    private static async Task RunCliCommand(string[] args)
    {
        var cmd = args[0].ToLowerInvariant();
        try
        {
            using var client = await ConnectToEngine();
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            await DrainInitialSnapshotAsync(reader);

            switch (cmd)
            {
                case "status":
                    await SendCommand(writer, new GetStatusMessage());
                    var status = await ReadUntilAsync<StatusUpdateMessage>(reader);
                    if (status is StatusUpdateMessage s)
                        Console.WriteLine($"Listening: {s.IsListening} | Mode: {s.Mode} | {s.StatusText} | Pipeline: {s.PipelineState}");
                    else
                        Console.Error.WriteLine("No status response from engine");
                    break;

                case "start":
                    await SendCommand(writer, new StartListeningMessage());
                    var ack = await ReadUntilAsync<AckMessage>(reader);
                    Console.WriteLine(ack is AckMessage a ? $"Start: {a.Success}" : "No response");
                    break;

                case "stop":
                    await SendCommand(writer, new StopListeningMessage());
                    var ack2 = await ReadUntilAsync<AckMessage>(reader);
                    Console.WriteLine(ack2 is AckMessage a2 ? $"Stop: {a2.Success}" : "No response");
                    break;

                case "cancel":
                    await SendCommand(writer, new CancelDictationMessage());
                    var ackC = await ReadUntilAsync<AckMessage>(reader);
                    Console.WriteLine(ackC is AckMessage ac
                        ? $"Cancel: {ac.Success}" + (ac.Error != null ? $" ({ac.Error})" : "")
                        : "No response");
                    break;

                case "reprocess":
                    await SendCommand(writer, new ReprocessLastMessage());
                    var ackR = await ReadUntilAsync<AckMessage>(reader);
                    if (ackR is AckMessage ar && ar.Success)
                        Console.WriteLine("Reprocessed: " + ar.Error); // JSON result
                    else
                        Console.WriteLine(ackR is AckMessage ar2
                            ? $"Reprocess failed: {ar2.Error}"
                            : "No response");
                    break;

                case "history":
                    await SendCommand(writer, new GetHistoryMessage { Limit = 10 });
                    var histAck = await ReadUntilAsync<AckMessage>(reader);
                    if (histAck is AckMessage ha && ha.Success && ha.Error != null)
                        Console.WriteLine(ha.Error); // JSON array of history entries
                    else
                        Console.Error.WriteLine("No history response from engine");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown command: {cmd}");
                    Console.Error.WriteLine("Usage: loquacio-tui [status|start|stop|cancel|reprocess|history|help|version]");
                    Environment.Exit(1);
                    break;
            }
        }
        catch (SocketException)
        {
            Console.Error.WriteLine("Cannot connect to engine. Is it running?");
            Environment.Exit(1);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Cannot connect to engine. Is it running?");
            Environment.Exit(1);
        }
    }

    // ── Interactive TUI ──

    private static async Task RunTui()
    {
        Console.Clear();
        DrawHeader();
        Console.WriteLine("Connecting to engine…");

        try
        {
            using var client = await ConnectToEngine();
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            await DrainInitialSnapshotAsync(reader);

            // Subscribe to updates
            await SendCommand(writer, new SubscribeMessage());

            // Start background reader
            var readCts = new CancellationTokenSource();
            var readTask = Task.Run(() => ReadLoopAsync(reader, readCts.Token));

            // Handle keyboard input
            ConsoleKeyInfo key;
            while (_running)
            {
                if (Console.KeyAvailable)
                {
                    key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.F1:
                            await SendCommand(writer, _isListening
                                ? new StopListeningMessage()
                                : new StartListeningMessage());
                            break;

                        case ConsoleKey.F2:
                                var nextMode = _mode switch
                                {
                                    ActivationMode.Continuous => ActivationMode.PushToTalk,
                                    _ => ActivationMode.Continuous // keyword activation is disabled — two-way cycle
                                };
                            await SendCommand(writer, new SetModeMessage { Mode = nextMode });
                            break;

                        case ConsoleKey.F3:
                            await SendCommand(writer, new CancelDictationMessage());
                            break;

                        case ConsoleKey.F4:
                            await SendCommand(writer, new GetHistoryMessage { Limit = 20 });
                            break;

                        case ConsoleKey.C when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        case ConsoleKey.Q:
                        case ConsoleKey.Escape:
                            _running = false;
                            break;
                    }
                }

                Render();
                await Task.Delay(100);
            }

            readCts.Cancel();
            await readTask;
        }
        catch (SocketException)
        {
            Console.WriteLine("\n Cannot connect to engine. Is it running?");
            Console.WriteLine(" Start it with: systemctl --user start loquacio");
            Environment.Exit(1);
        }
        catch (TimeoutException)
        {
            Console.WriteLine("\n Cannot connect to engine. Is it running?");
            Console.WriteLine(" Start it with: systemctl --user start loquacio");
            Environment.Exit(1);
        }
    }

    private static async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _running)
        {
            try
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                var msg = DeserializeMessage(line);
                if (msg == null) continue;

                switch (msg)
                {
                    case StatusUpdateMessage s:
                        _isListening = s.IsListening;
                        _mode = s.Mode;
                        _statusText = s.StatusText;
                        _pipelineState = s.PipelineState;
                        break;

                    case TranscriptionResultMessage t:
                        _history.Add((t.Text, t.Timestamp));
                        if (_history.Count > 50) _history.RemoveAt(0);
                        break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    // ── Rendering ──

    private static void Render()
    {
        Console.SetCursorPosition(0, 0);
        DrawHeader();

        var color = _isListening ? ConsoleColor.Green : ConsoleColor.Gray;
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"  Status: {_statusText,-40} Mode: {_mode}    ");
        Console.WriteLine($"  Pipeline: {_pipelineState,-40}                    ");
        Console.ForegroundColor = prev;

        Console.WriteLine("  ────────────────────────────────────────────────────────────────");

        // History
        Console.WriteLine("  Recent Transcriptions:");
        Console.WriteLine("  ────────────────────────────────────────────────────────────────");

        var shown = _history.TakeLast(15);
        foreach (var (text, time) in shown)
        {
            var preview = text.Length > 55 ? text[..52] + "…" : text;
            Console.WriteLine($"  {time:HH:mm:ss} │ {preview,-47}");
        }

        if (!_history.Any())
        {
            Console.WriteLine("  (no transcriptions yet)");
        }

        Console.WriteLine();
        Console.WriteLine("  F1: Start/Stop  │  F2: Cycle Mode  │  F3: Cancel Dictation  │  F4: Refresh History  │  Ctrl+C: Quit");
    }

    private static void DrawHeader()
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           🎙️  Loquacio — TUI Controller                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    // ── IPC helpers ──

    /// <summary>
    /// Connect to the engine's IPC endpoint: named pipe on Windows,
    /// Unix domain socket on Linux/macOS.
    /// </summary>
    private static async Task<Stream> ConnectToEngine()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", "loquacio", PipeDirection.InOut);
            await pipe.ConnectAsync(cts.Token);
            return pipe;
        }

        var path = GetSocketPath();
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static string GetSocketPath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "loquacio.sock");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "loquacio", "engine.sock");
    }

    private static async Task SendCommand(StreamWriter writer, IpcMessage msg)
    {
        var json = JsonSerializer.Serialize(msg, msg.GetType(), IpcJson.Options);
        await writer.WriteLineAsync(json);
    }

    /// <summary>
    /// Consume the status snapshot the engine pushes immediately after connect.
    /// On Windows the snapshot's pending write blocks client writes until it is
    /// read, so this must happen before the first command is sent.
    /// </summary>
    private static async Task DrainInitialSnapshotAsync(StreamReader reader)
    {
        var read = reader.ReadLineAsync();
        await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Reads messages until one of type <typeparamref name="T"/> arrives, skipping
    /// unsolicited broadcasts (e.g. the initial status snapshot sent on connect).
    /// </summary>
    private static async Task<IpcMessage?> ReadUntilAsync<T>(StreamReader reader, TimeSpan? timeout = null)
        where T : IpcMessage
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        while (!cts.Token.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (line == null) return null; // engine closed the connection
            if (DeserializeMessage(line) is T typed) return typed;
        }
        return null;
    }

    private static async Task<IpcMessage?> ReadMessage(StreamReader reader)
    {
        var line = await reader.ReadLineAsync();
        return line != null ? DeserializeMessage(line) : null;
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
                _ => null
            };
        }
        catch { return null; }
    }
}
