using NSubstitute;
using Loquacio.Services;
using Xunit;

namespace Loquacio.Tests;

/// <summary>
/// Tests for PorcupineKeywordDetectionService using a fake engine factory so no
/// Picovoice AccessKey or native runtime is needed (PLAN.md Phase 5.4).
/// </summary>
public class PorcupineKeywordDetectionServiceTests
{
    private sealed class FakeEngine : IPorcupineEngine
    {
        public int FrameLength { get; set; } = 512;
        public int SampleRate => 16000;
        public Func<short[], int> OnProcess { get; set; } = _ => -1;
        public bool Disposed { get; private set; }
        public List<short[]> Frames { get; } = [];
        public int Process(short[] pcm)
        {
            Frames.Add((short[])pcm.Clone());
            return OnProcess(pcm);
        }
        public void Dispose() => Disposed = true;
    }

    private static (PorcupineKeywordDetectionService Service, FakeEngine Engine) Create(
        Func<short[], int>? onProcess = null)
    {
        var engine = new FakeEngine { OnProcess = onProcess ?? (_ => -1) };
        var factory = Substitute.For<IPorcupineEngineFactory>();
        factory.Create(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(engine);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<PorcupineKeywordDetectionService>>();
        return (new PorcupineKeywordDetectionService(logger, factory), engine);
    }

    private static void SetAccessKey(string? value)
    {
        Environment.SetEnvironmentVariable("PICOVOICE_ACCESS_KEY", value);
    }

    [Fact]
    public void IsAvailable_WithoutAccessKey_ReturnsFalse()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey(null);
        try
        {
            var (service, _) = Create();
            Assert.False(service.IsAvailable);
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task StartAsync_WithAccessKey_CreatesEngineAndActivates()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var (service, _) = Create();
            await service.StartAsync("Jarvis");

            Assert.True(service.IsActive);
            await service.StopAsync();
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task StartAsync_WithAccessKeyAndCustomModel_PassesModelPathToFactory()
    {
        var prevKey = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        var prevModel = Environment.GetEnvironmentVariable("PICOVOICE_KEYWORD_PATH");
        SetAccessKey("test-key");
        Environment.SetEnvironmentVariable("PICOVOICE_KEYWORD_PATH", "/models/hey-dictate.ppn");
        try
        {
            var (service, _) = Create();
            await service.StartAsync("Hey Dictate");

            Assert.True(service.IsActive);
            await service.StopAsync();
        }
        finally
        {
            SetAccessKey(prevKey);
            Environment.SetEnvironmentVariable("PICOVOICE_KEYWORD_PATH", prevModel);
        }
    }

    [Fact]
    public async Task StartAsync_EngineCreationFails_StaysInactiveAndFiresOnError()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var factory = Substitute.For<IPorcupineEngineFactory>();
            factory.Create(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
                .Returns<IPorcupineEngine>(_ => throw new InvalidOperationException("bad key"));
            var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<PorcupineKeywordDetectionService>>();
            var service = new PorcupineKeywordDetectionService(logger, factory);

            Exception? seen = null;
            service.OnError += (_, ex) => seen = ex;

            await service.StartAsync("Jarvis");

            Assert.False(service.IsActive);
            Assert.IsType<InvalidOperationException>(seen);
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task ProcessAudioFrame_Detection_FiresKeywordDetectedOnce()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var (service, _) = Create(_ => 0); // every frame "detects"
            var detections = 0;
            service.KeywordDetected += (_, _) => detections++;

            await service.StartAsync("Jarvis");
            service.ProcessAudioFrame(new short[512]);

            // Cooldown suppresses the immediate second detection
            service.ProcessAudioFrame(new short[512]);
            Assert.Equal(1, detections);

            await service.StopAsync();
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task ProcessAudioFrame_RechunksArbitraryInputSizes()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var (service, engine) = Create();
            await service.StartAsync("Jarvis");

            // 1200 samples in odd-sized chunks -> exactly 2 full 512-sample frames
            var sent = 0;
            foreach (var chunk in new[] { 300, 7, 513, 380 })
            {
                service.ProcessAudioFrame(new short[chunk]);
                sent += chunk;
            }

            Assert.Equal(2, engine.Frames.Count);
            Assert.All(engine.Frames, f => Assert.Equal(512, f.Length));

            await service.StopAsync();
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task ProcessAudioFrame_EngineThrows_FiresOnErrorAndStops()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var (service, engine) = Create(_ => throw new IOException("native failure"));
            Exception? seen = null;
            service.OnError += (_, ex) => seen = ex;

            await service.StartAsync("Jarvis");
            service.ProcessAudioFrame(new short[512]);

            Assert.False(service.IsActive);
            Assert.IsType<IOException>(seen);
            Assert.True(engine.Disposed);
        }
        finally { SetAccessKey(prev); }
    }

    [Fact]
    public async Task Dispose_DisposesEngine()
    {
        var prev = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        SetAccessKey("test-key");
        try
        {
            var (service, engine) = Create();
            await service.StartAsync("Jarvis");
            service.Dispose();

            Assert.True(engine.Disposed);
        }
        finally { SetAccessKey(prev); }
    }
}
