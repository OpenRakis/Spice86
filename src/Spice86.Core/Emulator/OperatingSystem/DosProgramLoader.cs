namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Text;

internal class DosProgramLoader : DosFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    
    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state,DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21 = int21Handler;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        // Determine C drive base path
        string? cDrive = _configuration.CDrive;

        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = Path.GetDirectoryName(file) ?? "C:\\";
        }

        // Convert host file path to DOS path relative to C drive
        string absoluteDosPath = $"C:{file[cDrive.Length..]}";
        DosExecParameterBlock paramBlock = new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        _int21.LoadAndExecute(absoluteDosPath, paramBlock, commandTail: arguments);
        return File.ReadAllBytes(file);
    }
}
