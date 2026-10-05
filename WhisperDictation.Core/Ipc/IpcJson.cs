using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperDictation.Ipc;

/// <summary>
/// Shared JSON options for the IPC protocol (used by both <see cref="IpcServer"/>-side
/// hosts and <see cref="DaemonProxy"/>). Enums serialize as strings so external
/// clients can send e.g. "push-to-talk" instead of a raw number; numeric enum values
/// remain accepted on read for backward compatibility.
/// </summary>
public static class IpcJson
{
    /// <summary>kebab-case ("push-to-talk") for enum member names.</summary>
    private sealed class KebabNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) =>
            string.Concat(name.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter(new KebabNamingPolicy()) }
    };
}
