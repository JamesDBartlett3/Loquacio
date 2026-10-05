using System.Text.Json;
using Loquacio.Ipc;
using Loquacio.Models;

namespace Loquacio.Tests.Ipc;

/// <summary>
/// The IPC protocol serializes enums as strings (JsonStringEnumConverter) so
/// external clients can send "push-to-talk" instead of a raw number, while
/// numeric enum values remain accepted on read for backward compatibility.
/// </summary>
public class IpcJsonTests
{
    [Fact]
    public void Enums_SerializeAsStrings()
    {
        var msg = new StatusUpdateMessage { Mode = ActivationMode.PushToTalk };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);

        Assert.Contains("\"push-to-talk\"", json);
        Assert.DoesNotContain("\"Mode\":1", json);
    }

    [Fact]
    public void StringEnumValues_Deserialize()
    {
        var msg = JsonSerializer.Deserialize<StatusUpdateMessage>(
            """{"type":"status","isListening":true,"Mode":"push-to-talk"}""", IpcJson.Options);

        Assert.NotNull(msg);
        Assert.Equal(ActivationMode.PushToTalk, msg!.Mode);
    }

    [Fact]
    public void NumericEnumValues_StillDeserialize_BackwardCompatible()
    {
        var msg = JsonSerializer.Deserialize<StatusUpdateMessage>(
            """{"type":"status","isListening":true,"Mode":1}""", IpcJson.Options);

        Assert.NotNull(msg);
        Assert.Equal(ActivationMode.PushToTalk, msg!.Mode);
    }
}
