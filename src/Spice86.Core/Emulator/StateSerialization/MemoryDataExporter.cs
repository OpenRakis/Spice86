namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;

public class MemoryDataExporter {
    private readonly IMemory _memory;
    private readonly CallbackHandler _callbackHandler;
    private readonly MemoryDumpOptions _options;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="callbackHandler">The class that stores callback instructions.</param>
    /// <param name="options">Memory dump runtime options.</param>
    public MemoryDataExporter(IMemory memory, CallbackHandler callbackHandler, MemoryDumpOptions options) {
        _options = options;
        _memory = memory;
        _callbackHandler = callbackHandler;
    }

    /// <summary>
    /// Dumps the machine's memory to the file system.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    public void Write(string path) {
        File.WriteAllBytes(path, GenerateToolingCompliantRamDump());
    }

    public byte[] GenerateToolingCompliantRamDump() {
        if (_options.DosRuntimeState.InstallDosServices is true) {
            return _callbackHandler.ReplaceAllCallbacksInRamImage(_memory);
        }

        return _memory.ReadRam();
    }
}