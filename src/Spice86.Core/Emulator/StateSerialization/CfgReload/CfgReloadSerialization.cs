namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Single source of truth for the JSON options used to write and read the CFG reload artifact, so the
/// serialize and deserialize sides cannot drift (e.g. a converter added on write but missing on read).
/// </summary>
internal static class CfgReloadSerialization {
    /// <summary>Options shared by the reload writer and reader.</summary>
    public static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
