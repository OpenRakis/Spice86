namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// What a partitioned program knows about the hardware interrupts that can fire during its
/// execution: which vectors are hardware-triggered, the entry addresses of every emulator-installed
/// interrupt handler, where each hardware vector pointed when the snapshot was taken, the stub
/// address and service contracts of every installed vector and a description of every registered
/// callback.
/// </summary>
internal sealed record HardwareInterruptSetup {
    /// <summary>The interrupt vectors that are hardware-triggered (external events).</summary>
    public required IReadOnlySet<byte> HardwareVectorNumbers { get; init; }

    /// <summary>Entry addresses of every emulator-installed interrupt handler, whatever its vector.</summary>
    public required IReadOnlyList<SegmentedAddress> InstalledInterruptHandlerAddresses { get; init; }

    /// <summary>The IVT target of each hardware vector, read from memory when the snapshot is taken.</summary>
    public required IReadOnlyDictionary<byte, SegmentedAddress> HardwareVectorTargets { get; init; }

    /// <summary>The IVT target of each vector an installed hardware handler raises itself (the
    /// int 1Ch user hook of the int 8 stub), read from memory when the snapshot is taken.</summary>
    public required IReadOnlyDictionary<byte, SegmentedAddress> ChainedVectorTargets { get; init; }

    /// <summary>The installed service vectors, indexed by interrupt vector number.</summary>
    public required IReadOnlyDictionary<byte, InterruptServiceVector> ServiceVectors { get; init; }

    /// <summary>A one-line description of every registered callback, keyed by callback number.</summary>
    public required IReadOnlyDictionary<ushort, string> CallbackDescriptions { get; init; }

    /// <summary>A mouse user subroutine was registered at some point of the run, so the int 74h
    /// path may run program code at a mouse event.</summary>
    public bool MouseUserSubroutineRegistered { get; init; }

    /// <summary>Captures the setup from the installed handlers, the current interrupt vector
    /// table, the registered callbacks and whether a mouse user subroutine was registered during
    /// the run.</summary>
    public static HardwareInterruptSetup Capture(InstalledInterruptHandlers installed,
        InterruptVectorTable interruptVectorTable, CallbackHandler callbackHandler,
        bool mouseUserSubroutineRegistered) {
        Dictionary<byte, SegmentedAddress> targets = new();
        foreach (byte vector in installed.HardwareVectorNumbers) {
            targets[vector] = interruptVectorTable[vector];
        }
        Dictionary<byte, SegmentedAddress> chainedTargets = new();
        foreach (byte vector in installed.ChainedVectorNumbers) {
            chainedTargets[vector] = interruptVectorTable[vector];
        }
        return new HardwareInterruptSetup {
            HardwareVectorNumbers = new HashSet<byte>(installed.HardwareVectorNumbers),
            InstalledInterruptHandlerAddresses = [.. installed.Addresses],
            HardwareVectorTargets = targets,
            ChainedVectorTargets = chainedTargets,
            ServiceVectors = CaptureServiceVectors(installed),
            CallbackDescriptions = Emulator.InterruptHandlers.Common.Callback.CallbackDescriptions.Describe(callbackHandler),
            MouseUserSubroutineRegistered = mouseUserSubroutineRegistered
        };
    }

    private static Dictionary<byte, InterruptServiceVector> CaptureServiceVectors(
        InstalledInterruptHandlers installed) {
        Dictionary<byte, InterruptServiceVector> vectors = new();
        foreach ((byte vector, IInterruptHandler handler) in installed.Handlers) {
            if (!installed.TryGetInstalledAddress(vector, out SegmentedAddress stub)) {
                continue;
            }
            vectors[vector] = new InterruptServiceVector(vector, stub, handler.Services,
                handler.GetType());
        }
        return vectors;
    }
}
