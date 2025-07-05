namespace Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Linq;
using System.Text;

/// <summary>
/// A class that generates a process's environment block.
/// </summary>
public class EnvironmentBlockGenerator {
    private readonly EnvironmentVariables _environmentVariables;

    /// <summary>
    /// Initializes a new instance of the EnvironmentBlockGenerator class.
    /// </summary>
    /// <param name="environmentVariables">The master environment block from the DOS kernel.</param>
    public EnvironmentBlockGenerator(EnvironmentVariables environmentVariables) => _environmentVariables = environmentVariables;

    /// <summary>
    /// Returns a byte array containing a process's environment block.
    /// </summary>
    /// <returns>Byte array containing the process's environment block.</returns>
    public byte[] BuildEnvironmentBlock(string dosFileSpec) {
        byte[] environmentStrings = _environmentVariables.EnvironmentBlock;

        byte[] dosFileSpecBytes = Encoding.ASCII.GetBytes(dosFileSpec);
        // Need 2 bytes between strings and path and a null terminator after path.
        byte[] fullBlock = new byte[environmentStrings.Length + 2 + dosFileSpecBytes.Length + 1];

        environmentStrings.CopyTo(fullBlock, 0);

        dosFileSpecBytes.CopyTo(fullBlock, environmentStrings.Length + 2);

        //dosFileSpec is a zero-terminated ASCII string
        fullBlock[environmentStrings.Length + 2 + dosFileSpecBytes.Length] = 0;

        //16-bit binary count of additional strings
        //Normally, it is 001H
        fullBlock[environmentStrings.Length] = 1;


        return fullBlock;
    }
}
