namespace Spice86.Emulator.LoadableFile.Dos;

using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class EnvironmentBlockGenerator {
    private readonly Machine _machine;
    public EnvironmentBlockGenerator(Machine machine)  => _machine = machine;


    /// <summary>
    /// Returns a byte array containing a process's environment block.
    /// </summary>
    /// <returns>Byte array containing the process's environment block.</returns>
    public byte[] BuildEnvironmentBlock() {
        byte[] environmentStrings = _machine.EnvironmentVariables.GetEnvironmentBlock();
        // Need 2 bytes between strings and path and a null terminator after path.
        byte[] fullBlock = new byte[environmentStrings.Length + 2];

        environmentStrings.CopyTo(fullBlock, 0);

        // Not sure what this is for.
        fullBlock[environmentStrings.Length] = 1;

        return fullBlock;
    }
}
