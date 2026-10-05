using System.Text.Json;
using WhisperDictation.Ipc;

namespace WhisperDictation.Tests.Ipc;

public class IpcMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void StatusUpdateMessage_RoundTrips()
    {
        var msg = new StatusUpdateMessage
        {
            IsListening = true,
            Mode = ActivationMode.PushToTalk,
            AudioLevel = 0.75,
            StatusText = "Listening",
            StatusColor = "#2196F3",
            CorrelationId = "test-123"
        };

        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<StatusUpdateMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("status", deserialized.MessageType);
        Assert.Equal("test-123", deserialized.CorrelationId);
        Assert.True(deserialized.IsListening);
        Assert.Equal(ActivationMode.PushToTalk, deserialized.Mode);
        Assert.Equal(0.75, deserialized.AudioLevel);
        Assert.Equal("Listening", deserialized.StatusText);
        Assert.Equal("#2196F3", deserialized.StatusColor);
    }

    [Fact]
    public void StatusUpdateMessage_IncludesPipelineStateAndSession()
    {
        var msg = new StatusUpdateMessage
        {
            IsListening = true,
            Mode = ActivationMode.Continuous,
            StatusText = "Listening · Transcribing",
            StatusColor = "#eab308",
            PipelineState = "Transcribing",
            SessionId = "d0f4a1b2-1111-2222-3333-444455556666"
        };

        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<StatusUpdateMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("Transcribing", deserialized.PipelineState);
        Assert.Equal("d0f4a1b2-1111-2222-3333-444455556666", deserialized.SessionId);
    }

    [Fact]
    public void PipelineStatusStyle_MapsStagesToTextAndColor()
    {
        Assert.Equal(("Idle", "#808080"), PipelineStatusStyle.Describe(WhisperDictation.Infrastructure.DictationState.Transcribing, isListening: false));
        Assert.Equal(("Listening", "#22c55e"), PipelineStatusStyle.Describe(WhisperDictation.Infrastructure.DictationState.Idle, isListening: true));
        Assert.Equal(("Listening · Transcribing", "#eab308"), PipelineStatusStyle.Describe(WhisperDictation.Infrastructure.DictationState.Transcribing, isListening: true));
        Assert.Equal(("Listening · Saving", "#3b82f6"), PipelineStatusStyle.Describe(WhisperDictation.Infrastructure.DictationState.Saving, isListening: true));
    }

    [Fact]
    public void TranscriptionResultMessage_RoundTrips()
    {
        var msg = new TranscriptionResultMessage
        {
            Text = "Hello world this is a test",
            Timestamp = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromHours(-5)),
            IsFinal = true
        };

        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<TranscriptionResultMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("transcription", deserialized.MessageType);
        Assert.Equal("Hello world this is a test", deserialized.Text);
        Assert.True(deserialized.IsFinal);
    }

    [Fact]
    public void SubscribeMessage_Serializes()
    {
        var msg = new SubscribeMessage();
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<SubscribeMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("subscribe", deserialized.MessageType);
    }

    [Fact]
    public void StartListeningMessage_Serializes()
    {
        var msg = new StartListeningMessage { CorrelationId = "start-1" };
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<StartListeningMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("start-listening", deserialized.MessageType);
        Assert.Equal("start-1", deserialized.CorrelationId);
    }

    [Fact]
    public void SetModeMessage_RoundTrips()
    {
        var msg = new SetModeMessage { Mode = ActivationMode.KeywordActivated };
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<SetModeMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("set-mode", deserialized.MessageType);
        Assert.Equal(ActivationMode.KeywordActivated, deserialized.Mode);
    }

    [Fact]
    public void AckMessage_RoundTrips()
    {
        var msg = new AckMessage { Success = true, Error = null, CorrelationId = "req-42" };
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<AckMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("ack", deserialized.MessageType);
        Assert.True(deserialized.Success);
        Assert.Null(deserialized.Error);
    }

    [Fact]
    public void AckMessage_WithError_RoundTrips()
    {
        var msg = new AckMessage { Success = false, Error = "Not connected" };
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<AckMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("Not connected", deserialized.Error);
    }

    [Fact]
    public void AllMessageTypes_HaveDistinctMessageType()
    {
        var messages = new IpcMessage[]
        {
            new StatusUpdateMessage(),
            new TranscriptionResultMessage(),
            new SettingsChangedMessage(),
            new SubscribeMessage(),
            new GetStatusMessage(),
            new GetHistoryMessage(),
            new UpdateSettingsMessage(),
            new StartListeningMessage(),
            new StopListeningMessage(),
            new SetModeMessage(),
            new AckMessage(),
        };

        var typeSet = messages.Select(m => m.MessageType).ToHashSet();
        Assert.Equal(messages.Length, typeSet.Count);
    }
}
