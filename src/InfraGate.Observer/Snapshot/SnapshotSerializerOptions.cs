using System.Text.Encodings.Web;

namespace InfraGate.Observer.Snapshot;

internal static class SnapshotSerializerOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
