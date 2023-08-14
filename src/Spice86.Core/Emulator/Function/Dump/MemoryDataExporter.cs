namespace Spice86.Core.Emulator.Function.Dump;

using Serilog.Events;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class MemoryDataExporter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly CallbackHandler _callbackHandler;
    private readonly Configuration _configuration;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="callbackHandler">The class that stores callback instructions.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="dumpDirectory">Where to dump the data.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public MemoryDataExporter(IMemory memory, CallbackHandler callbackHandler, Configuration configuration,
        string dumpDirectory, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
        _configuration = configuration;
        _memory = memory;
        _callbackHandler = callbackHandler;
    }

    /// <summary>
    /// Dumps the machine's memory to the file system.
    /// </summary>
    /// <param name="suffix">The suffix to add to the file name.</param>
    public void DumpMemory(string suffix) {
        string path = GenerateDumpFileName($"MemoryDump{suffix}.bin");
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Dumping memory content in file {Path}", path);
        }

        File.WriteAllBytes(path, GenerateToolingCompliantRamDump());
    }

    private byte[] GenerateToolingCompliantRamDump() {
        if (_configuration.InitializeDOS is true) {
            return _callbackHandler.ReplaceAllCallbacksInRamImage(_memory);
        }

        return _memory.RamCopy;
    }
}