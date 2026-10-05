using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WhisperDictation.Daemon.Ipc;
using WhisperDictation.Daemon.Services;
using WhisperDictation.Infrastructure;

namespace WhisperDictation.Tests.Daemon;

public class DaemonPipelineTests
{
    private readonly IActivationManagerService _activation;
    private readonly IBackgroundTranscriptionService _transcription;
    private readonly ITextInjectionService _textInjection;
    private readonly IHistoryService _history;
    private readonly ILLMPostProcessorService _llm;
    private readonly IpcServer _ipcServer;
    private readonly ILogger<DaemonPipeline> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DaemonPipelineTests()
    {
        _activation = Substitute.For<IActivationManagerService>();
        _transcription = Substitute.For<IBackgroundTranscriptionService>();
        _textInjection = Substitute.For<ITextInjectionService>();
        _history = Substitute.For<IHistoryService>();
        _llm = Substitute.For<ILLMPostProcessorService>();
        _ipcServer = Substitute.For<IpcServer>(null!, null!, (string?)null);
        _logger = Substitute.For<ILogger<DaemonPipeline>>();
        _serviceProvider = Substitute.For<IServiceProvider>();
    }

    private DaemonPipeline CreatePipeline() => new(
        _activation, _transcription, _history, _llm, _ipcServer, _logger);

    [Fact]
    public async Task InitializeAsync_WiresTranscriptionEvents()
    {
        var pipeline = CreatePipeline();

        await pipeline.InitializeAsync();

        _transcription.Received(1).OnTranscriptionCompleted += Arg.Any<EventHandler<TranscriptionResult>>();
        _transcription.Received(1).OnError += Arg.Any<EventHandler<Exception>>();
        await _activation.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var pipeline = CreatePipeline();

        await pipeline.InitializeAsync();
        await pipeline.InitializeAsync(); // second call should be a no-op

        _transcription.Received(1).OnTranscriptionCompleted += Arg.Any<EventHandler<TranscriptionResult>>();
        await _activation.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShutdownAsync_UnwiresEvents()
    {
        var pipeline = CreatePipeline();
        await pipeline.InitializeAsync();

        await pipeline.ShutdownAsync();

        _transcription.Received(1).OnTranscriptionCompleted -= Arg.Any<EventHandler<TranscriptionResult>>();
        _transcription.Received(1).OnError -= Arg.Any<EventHandler<Exception>>();
        await _activation.Received(1).ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownAsync_WithoutInit_IsNoOp()
    {
        var pipeline = CreatePipeline();

        await pipeline.ShutdownAsync(); // should not throw

        await _activation.DidNotReceive().ShutdownAsync();
    }

    [Fact]
    public async Task InitializeAsync_SetsIsRunningViaActivationManager()
    {
        var pipeline = CreatePipeline();
        _activation.IsListening.Returns(true);

        await pipeline.InitializeAsync();

        Assert.True(pipeline.IsRunning);
    }

    [Fact]
    public async Task InitializeAsync_IsRunningFalse_WhenNotListening()
    {
        var pipeline = CreatePipeline();
        _activation.IsListening.Returns(false);

        await pipeline.InitializeAsync();

        Assert.False(pipeline.IsRunning);
    }

    [Fact]
    public async Task InitializeAsync_SubscribesPipelineStateChanged()
    {
        var pipeline = CreatePipeline();

        await pipeline.InitializeAsync();

        _transcription.Received(1).PipelineStateChanged += Arg.Any<Action<DictationStateChangedEventArgs>>();
    }
}
