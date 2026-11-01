namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;

using System.Linq;

/// <summary>
/// Captures the <see cref="GeneratorInputs"/> of a machine when a snapshot is built: the one
/// place that reads the installed handlers, the interrupt vector table, the registered
/// callbacks, the loaded images and entry point, the mouse user-subroutine flag, the changed
/// bytes, the address space and the registered I/O ports.
/// </summary>
internal sealed class GeneratorInputsCollector {
    private readonly InstalledInterruptHandlers _installedInterruptHandlers;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly CallbackHandler _callbackHandler;
    private readonly LoadedImageRegistry _loadedImageRegistry;
    private readonly SharedMouseData _sharedMouseData;
    private readonly ChangedMemoryMap _changedMemoryMap;
    private readonly IMemory _memory;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly ushort _programEntryPointSegment;

    public GeneratorInputsCollector(InstalledInterruptHandlers installedInterruptHandlers,
        InterruptVectorTable interruptVectorTable, CallbackHandler callbackHandler,
        LoadedImageRegistry loadedImageRegistry, SharedMouseData sharedMouseData,
        ChangedMemoryMap changedMemoryMap, IMemory memory, IOPortDispatcher ioPortDispatcher,
        ushort programEntryPointSegment) {
        _installedInterruptHandlers = installedInterruptHandlers;
        _interruptVectorTable = interruptVectorTable;
        _callbackHandler = callbackHandler;
        _loadedImageRegistry = loadedImageRegistry;
        _sharedMouseData = sharedMouseData;
        _changedMemoryMap = changedMemoryMap;
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
        _programEntryPointSegment = programEntryPointSegment;
    }

    /// <summary>Reads every machine input a snapshot partitioning needs.</summary>
    public GeneratorInputs Capture() => new(
        HardwareInterruptSetup.Capture(_installedInterruptHandlers, _interruptVectorTable,
            _callbackHandler, _sharedMouseData.UserSubroutineEverRegistered),
        ProgramImages.Create(_programEntryPointSegment, _loadedImageRegistry.Images),
        _changedMemoryMap.Snapshot(),
        _memory.ReadRam(),
        _ioPortDispatcher.RegisteredHandlers.Select(pair => (ushort)pair.Key).ToHashSet(),
        _loadedImageRegistry.ProgramEntryPoint);
}
