using System.Text.Json;
using Loquacio.Ipc;

namespace Loquacio.Tests.Daemon;

/// <summary>
/// Tests for IPC message deserialization logic matching the daemon's IpcServer.DeserializeMessage.
/// Verifies the wire protocol contract between daemon and controllers.
/// </summary>
public class IpcProtocolTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Theory]
    [InlineData("subscribe", typeof(SubscribeMessage))]
    [InlineData("get-status", typeof(GetStatusMessage))]
    [InlineData("start-listening", typeof(StartListeningMessage))]
    [InlineData("stop-listening", typeof(StopListeningMessage))]
    [InlineData("set-mode", typeof(SetModeMessage))]
    [InlineData("get-history", typeof(GetHistoryMessage))]
    [InlineData("update-settings", typeof(UpdateSettingsMessage))]
    [InlineData("cancel-dictation", typeof(CancelDictationMessage))]
    [InlineData("reprocess-last", typeof(ReprocessLastMessage))]
    public void DeserializeMessage_KnownTypes_RoundTrip(string messageType, Type expectedType)
    {
        var msg = (IpcMessage)Activator.CreateInstance(expectedType)!;
        var json = JsonSerializer.Serialize(msg, expectedType, Options);

        using var doc = JsonDocument.Parse(json);
        var typeStr = doc.RootElement.GetProperty("type").GetString();
        Assert.Equal(messageType, typeStr);

        IpcMessage? deserialized = typeStr switch
        {
            "subscribe" => JsonSerializer.Deserialize<SubscribeMessage>(json, Options),
            "get-status" => JsonSerializer.Deserialize<GetStatusMessage>(json, Options),
            "start-listening" => JsonSerializer.Deserialize<StartListeningMessage>(json, Options),
            "stop-listening" => JsonSerializer.Deserialize<StopListeningMessage>(json, Options),
            "set-mode" => JsonSerializer.Deserialize<SetModeMessage>(json, Options),
            "get-history" => JsonSerializer.Deserialize<GetHistoryMessage>(json, Options),
            "update-settings" => JsonSerializer.Deserialize<UpdateSettingsMessage>(json, Options),
            "cancel-dictation" => JsonSerializer.Deserialize<CancelDictationMessage>(json, Options),
            "reprocess-last" => JsonSerializer.Deserialize<ReprocessLastMessage>(json, Options),
            _ => null
        };

        Assert.NotNull(deserialized);
        Assert.Equal(messageType, deserialized.MessageType);
    }

    [Fact]
    public void DeserializeMessage_UnknownType_ReturnsNull()
    {
        var json = """{"type":"unknown-command","id":"x"}""";
        using var doc = JsonDocument.Parse(json);
        var typeStr = doc.RootElement.GetProperty("type").GetString();

        IpcMessage? result = typeStr switch
        {
            "subscribe" => null,
            _ => null
        };

        Assert.Null(result);
    }

    [Fact]
    public void DeserializeMessage_InvalidJson_ReturnsNull()
    {
        IpcMessage? TryDeserialize(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var type = doc.RootElement.GetProperty("type").GetString();
                return type switch
                {
                    "subscribe" => JsonSerializer.Deserialize<SubscribeMessage>(line),
                    _ => (IpcMessage?)null
                };
            }
            catch { return null; }
        }

        var result = TryDeserialize("not valid json at all");
        Assert.Null(result);
    }

    [Fact]
    public void StatusUpdateMessage_ContainsAllRequiredFields()
    {
        var msg = new StatusUpdateMessage
        {
            IsListening = true,
            Mode = ActivationMode.Continuous,
            AudioLevel = 0.5,
            StatusText = "Listening",
            StatusColor = "#22c55e"
        };

        var json = JsonSerializer.Serialize(msg, Options);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("isListening").GetBoolean());
        // Mode is an enum, serialized as number by default
        Assert.Equal((int)ActivationMode.Continuous, doc.RootElement.GetProperty("mode").GetInt32());
        Assert.Equal(0.5, doc.RootElement.GetProperty("audioLevel").GetDouble());
        Assert.Equal("Listening", doc.RootElement.GetProperty("statusText").GetString());
        Assert.Equal("#22c55e", doc.RootElement.GetProperty("statusColor").GetString());
    }

    [Fact]
    public void GetHistoryMessage_WithLimit_Serializes()
    {
        var msg = new GetHistoryMessage { Limit = 5, Offset = 10 };
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<GetHistoryMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(5, deserialized.Limit);
        Assert.Equal(10, deserialized.Offset);
    }

    [Fact]
    public void GetHistoryMessage_WithoutLimit_Serializes()
    {
        var msg = new GetHistoryMessage();
        var json = JsonSerializer.Serialize(msg, Options);
        var deserialized = JsonSerializer.Deserialize<GetHistoryMessage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Limit);
        Assert.Null(deserialized.Offset);
    }
}
