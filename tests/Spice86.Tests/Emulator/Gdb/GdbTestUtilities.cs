namespace Spice86.Tests.Emulator.Gdb;

/// <summary>
/// Utility methods for GDB protocol testing.
/// </summary>
internal static class GdbTestUtilities {
    /// <summary>
    /// Extracts the payload from a GDB protocol response message.
    /// Response format: +$payload#checksum
    /// </summary>
    /// <param name="response">The full GDB protocol response string</param>
    /// <returns>The payload portion between $ and # delimiters</returns>
    public static string ExtractPayload(string response) {
        int dollarIndex = response.IndexOf('$');
        int hashIndex = response.IndexOf('#');
        if (dollarIndex >= 0 && hashIndex > dollarIndex) {
            return response.Substring(dollarIndex + 1, hashIndex - dollarIndex - 1);
        }
        return string.Empty;
    }
}
