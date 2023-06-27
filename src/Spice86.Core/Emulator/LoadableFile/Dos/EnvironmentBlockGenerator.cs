namespace Spice86.Core.Emulator.LoadableFile.Dos;

using Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// A class that generates a process's environment block.
/// </summary>
public class EnvironmentBlockGenerator {
    private readonly Dos _dos;
    
    /// <summary>
    /// Initializes a new instance of the EnvironmentBlockGenerator class.
    /// </summary>
    /// <param name="dos">The DOS kernel to generate an environment block for.</param>
    public EnvironmentBlockGenerator(Dos dos) => _dos = dos;

    /// <summary>
    /// Returns a byte array containing a process's environment block.
    /// </summary>
    /// <returns>Byte array containing the process's environment block.</returns>
    public byte[] BuildEnvironmentBlock() {
        byte[] environmentStrings = _dos.EnvironmentVariables.EnvironmentBlock;
        // Need 2 bytes between strings and path and a null terminator after path.
        byte[] fullBlock = new byte[environmentStrings.Length + 2];

        environmentStrings.CopyTo(fullBlock, 0);

        // Not sure what this is for.
        fullBlock[environmentStrings.Length] = 1;

        return fullBlock;
    }
}
