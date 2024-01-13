namespace Spice86.Core.Emulator.LoadableFile;

using System.IO;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Base class for loading executable files in the VM like exe, bios, ...
/// </summary>
public abstract class ExecutableFileLoader {
    /// <summary>
    /// The emulator CPU.
    /// </summary>
    protected State _state;

    /// <summary>
    /// The memory bus.
    /// </summary>
    protected IMemory _memory;

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutableFileLoader"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU Registers and Flags.</param>
    /// <param name="loggerService">The <see cref="ILoggerService"/> instance.</param>
    protected ExecutableFileLoader(IMemory memory, State state, ILoggerService loggerService) {
        _loggerService = loggerService;
        _memory = memory;
        _state = state;
    }

    /// <summary>
    /// Gets a value indicating whether DOS initialization is needed.
    /// </summary>
    public abstract bool DosInitializationNeeded { get; }

    /// <summary>
    /// Loads an executable file and returns its bytes.
    /// </summary>
    /// <param name="file">The path of the file to load.</param>
    /// <param name="arguments">Optional arguments to pass to the loaded file.</param>
    /// <returns>The bytes of the loaded file.</returns>
    public abstract byte[] LoadFile(string file, string? arguments);

    /// <summary>
    /// Reads the contents of a file and returns its bytes.
    /// </summary>
    /// <param name="file">The path of the file to read.</param>
    /// <returns>The bytes of the read file.</returns>
    protected byte[] ReadFile(string file) {
        return File.ReadAllBytes(file);
    }

    /// <summary>
    /// Sets the entry point of the loaded file to the specified segment and offset values.
    /// </summary>
    /// <param name="cs">The segment value of the entry point.</param>
    /// <param name="ip">The offset value of the entry point.</param>
    protected void SetEntryPoint(ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program entry point is {ProgramEntry}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }
}