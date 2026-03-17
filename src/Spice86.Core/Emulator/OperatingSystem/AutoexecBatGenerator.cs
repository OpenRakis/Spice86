namespace Spice86.Core.Emulator.OperatingSystem;

using System.Text;

/// <summary>
/// Generates AUTOEXEC.BAT content for the Z: drive.
/// </summary>
public class AutoexecBatGenerator {
    /// <summary>
    /// Generates AUTOEXEC.BAT content for a given program.
    /// </summary>
    /// <param name="programPath">The path to the program to execute (e.g., "C:\\PROGRAM.EXE").</param>
    /// <returns>AUTOEXEC.BAT content as UTF-8 encoded bytes.</returns>
    public byte[] Generate(string programPath) {
        StringBuilder sb = new StringBuilder();

        // DOS batch preamble
        sb.AppendLine("@ECHO OFF");
        sb.AppendLine();

        // Execute the requested program
        sb.AppendLine($"CALL {programPath}");
        sb.AppendLine();

        // Exit with the program's exit code
        sb.AppendLine("EXIT");

        // Encode as ASCII (CP437 compatible for basic characters)
        // Use CRLF line endings for DOS compatibility
        string content = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        return Encoding.ASCII.GetBytes(content);
    }
}
