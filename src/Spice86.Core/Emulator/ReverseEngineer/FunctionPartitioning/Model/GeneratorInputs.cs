namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The machine state a snapshot partitioning needs, captured once from the live machine: the
/// hardware interrupt setup, the loaded program images, the changed-bytes snapshot, a copy of
/// the address space, the registered I/O ports and the program's entry point.
/// </summary>
internal sealed record GeneratorInputs(
    HardwareInterruptSetup HardwareInterrupts,
    ProgramImages ProgramImages,
    ChangedMemorySnapshot ChangedMemory,
    byte[] MemorySnapshot,
    IReadOnlyDictionary<ushort, Type> IoPorts,
    SegmentedAddress? ProgramEntryPoint) {
    /// <summary>The inputs of a program built without a machine: no handler, no image, no changed
    /// bytes, no memory and no entry point.</summary>
    internal static GeneratorInputs Empty { get; } = new(
        new HardwareInterruptSetup {
            HardwareVectorNumbers = new HashSet<byte>(),
            InstalledInterruptHandlerAddresses = [],
            HardwareVectorTargets = new Dictionary<byte, SegmentedAddress>(),
            ChainedVectorTargets = new Dictionary<byte, SegmentedAddress>(),
            ServiceVectors = new Dictionary<byte, InterruptServiceVector>(),
            CallbackDescriptions = new Dictionary<ushort, string>(),
            MouseUserSubroutineRegistered = false
        },
        new ProgramImages(0, []),
        new ChangedMemorySnapshot { WatchedAreas = [], ChangedRanges = [] },
        [],
        new Dictionary<ushort, Type>(),
        null);
}
