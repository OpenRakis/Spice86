namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.IO;

internal class DosProgramLoader : DosFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    private readonly DosProcessManager _processManager;
    
    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21 = int21Handler;
        _processManager = int21Handler.ProcessManager;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        // Ensure root COMMAND.COM PSP exists before loading any programs
        _processManager.CreateRootCommandComPsp();
        
        // Mark the next program as the initial program so that when it terminates,
        // the emulator halts instead of trying to return to a non-existent parent
        _processManager.MarkNextProgramAsInitial();
        
        // Determine C drive base path
        string? cDrive = _configuration.CDrive;

        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = Path.GetDirectoryName(file) ?? "C:\\";
        }

        // Convert host file path to DOS path relative to C drive
        string absoluteDosPath;

        if (file.Length >= cDrive.Length) {
            absoluteDosPath = $"C:{file[cDrive.Length..]}";
        } else {
            string fileName = Path.GetFileName(file);
            absoluteDosPath = $"C:{fileName}";
        }

        DosExecParameterBlock paramBlock = new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        _int21.LoadAndExecute(absoluteDosPath, paramBlock, commandTail: arguments);
        return File.ReadAllBytes(file);
    }
}
