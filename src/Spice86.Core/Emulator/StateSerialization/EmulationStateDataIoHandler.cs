namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Shared.Interfaces;

/// <summary>
/// An abstract class that provides a base implementation for recording execution data.
/// </summary>
public abstract class EmulationStateDataIoHandler {
    private readonly EmulatorStateSerializationFolder _emulatorStateSerializationFolder;

    protected ILoggerService LoggerService { get; }

    /// <summary>
    /// The directory where data will be saved.
    /// </summary>
    protected string DataDirectory => _emulatorStateSerializationFolder.Folder;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulationStateDataIoHandler"/> class with the specified dump directory.
    /// </summary>
    /// <param name="emulatorStateSerializationFolder">Where data will be saved / loaded.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected EmulationStateDataIoHandler(EmulatorStateSerializationFolder emulatorStateSerializationFolder, ILoggerService loggerService) {
        LoggerService = loggerService;
        _emulatorStateSerializationFolder = emulatorStateSerializationFolder;
    }

    /// <summary>
    /// Gets name of the file the execution flow dump.
    /// </summary>
    protected string ExecutionFlowFile => GenerateDumpFileName("ExecutionFlow.json");

    /// <summary>
    /// Gets name of the file the Ghidra symbols dump.
    /// </summary>
    protected string SymbolsFile => GenerateDumpFileName("GhidraSymbols.txt");

    /// <summary>
    /// Gets name of the file the Cpu registers dump.
    /// </summary>
    protected string CpuRegistersFile => GenerateDumpFileName($"CpuRegisters.json");
    
    /// <summary>
    /// Gets name of the file the memory dump.
    /// </summary>
    protected string MemoryFile => GenerateDumpFileName($"MemoryDump.bin");
    
    /// <summary>
    /// Gets name of the file the ASM listing dump.
    /// </summary>
    protected string ListingFile => GenerateDumpFileName($"Listing.asm");
    
    /// <summary>
    /// Gets name of the file the ASM listing dump.
    /// </summary>
    protected string BreakpointsFile => $"{DataDirectory}/Breakpoints.json";

    /// <summary>
    /// Generates a dump file name with the specified suffix.
    /// </summary>
    /// <param name="suffix">The suffix to add to the dump file name.</param>
    /// <returns>A dump file name with the specified suffix.</returns>
    protected string GenerateDumpFileName(string suffix) {
        return $"{DataDirectory}/spice86dump{suffix}";
    }
}
