namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Shared parsing of <c>segment:offset</c> addresses stored in a CFG reload dump. Centralizes the
/// "parse or fail loudly" contract so the reconstructor and the reloader do not each re-spell it.
/// </summary>
internal static class ReloadAddressParser {
    /// <summary>
    /// Parses a dumped <c>segment:offset</c> address, throwing <see cref="InvalidOperationException"/>
    /// with <paramref name="context"/> in the message when the value is malformed.
    /// </summary>
    public static SegmentedAddress ParseOrThrow(string address, string context) {
        if (!SegmentedAddress.TryParse(address, out SegmentedAddress? parsed)) {
            throw new InvalidOperationException($"Could not parse reload {context} '{address}'");
        }
        return parsed.Value;
    }
}
