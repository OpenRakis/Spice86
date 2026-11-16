namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Reflection;

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

    /// <summary>The (vector, handler) pair of every hardware interrupt delivered during the run:
    /// a program can re-hook a vector, so its delivery targets are not recoverable from the
    /// snapshot IVT alone.</summary>
    public required IReadOnlyList<(byte Vector, SegmentedAddress Target)> HardwareInterruptDeliveries { get; init; }

    /// <summary>The IVT target of each vector an installed hardware handler raises itself (the
    /// int 1Ch user hook of the int 8 stub), read from memory when the snapshot is taken.</summary>
    public required IReadOnlyDictionary<byte, SegmentedAddress> ChainedVectorTargets { get; init; }

    /// <summary>The installed service vectors, indexed by interrupt vector number.</summary>
    public required IReadOnlyDictionary<byte, InterruptServiceVector> ServiceVectors { get; init; }

    /// <summary>A one-line description of every registered callback, keyed by callback number.</summary>
    public required IReadOnlyDictionary<ushort, string> CallbackDescriptions { get; init; }

    /// <summary>The service contract of every registered callback whose contract is known
    /// (a single-service interrupt handler's <c>Run</c>, or a method carrying exactly one
    /// selectorless <see cref="InterruptServiceAttribute"/>), keyed by callback number.</summary>
    public required IReadOnlyDictionary<ushort, ServiceContract> CallbackContracts { get; init; }

    /// <summary>Addresses of the call instructions an <see cref="IPatchedCallSite"/>'s address
    /// switcher can rewrite at run time (the int 74h stub's driver call and the mouse driver's
    /// user-routine call). Generated code verifies their bytes before running them.</summary>
    public required IReadOnlySet<SegmentedAddress> SwitcherPatchedCalls { get; init; }

    /// <summary>A mouse user subroutine was registered at some point of the run, so the int 74h
    /// path may run program code at a mouse event.</summary>
    public bool MouseUserSubroutineRegistered { get; init; }

    /// <summary>The program set the current PSP itself through DOS AH=50h or created a child PSP
    /// through AH=55h during the run, so a terminate can resume the parent through an interrupt
    /// frame a typed call wrote; every typed service call of such a program keeps writing the
    /// frame.</summary>
    public bool ProgramSetCurrentPsp { get; init; }

    /// <summary>A terminate during the run resumed by the batch engine launching the next
    /// program: that resume only exists as the interrupt stub's callback-moved-CS:IP path, so
    /// generated code of such a program keeps terminate calls untyped.</summary>
    public bool TerminateResumedBatchLaunch { get; init; }

    /// <summary>Captures the setup from the installed handlers, the current interrupt vector
    /// table, the registered callbacks and whether a mouse user subroutine was registered during
    /// the run.</summary>
    public static HardwareInterruptSetup Capture(InstalledInterruptHandlers installed,
        InterruptVectorTable interruptVectorTable, CallbackHandler callbackHandler,
        bool mouseUserSubroutineRegistered,
        IEnumerable<(byte Vector, SegmentedAddress Target)> hardwareInterruptDeliveries) {
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
            HardwareInterruptDeliveries = [.. hardwareInterruptDeliveries],
            ChainedVectorTargets = chainedTargets,
            ServiceVectors = CaptureServiceVectors(installed),
            CallbackDescriptions = Emulator.InterruptHandlers.Common.Callback.CallbackDescriptions.Describe(callbackHandler),
            CallbackContracts = CaptureCallbackContracts(callbackHandler, mouseUserSubroutineRegistered),
            SwitcherPatchedCalls = CaptureSwitcherPatchedCalls(callbackHandler),
            MouseUserSubroutineRegistered = mouseUserSubroutineRegistered,
            ProgramSetCurrentPsp = installed.Handlers.Values
                .OfType<DosInt21Handler>().Any(handler => handler.ProgramSetCurrentPsp),
            TerminateResumedBatchLaunch = installed.Handlers.Values
                .OfType<DosInt21Handler>().Any(handler => handler.TerminateResumedBatchLaunch)
        };
    }

    private static Dictionary<ushort, ServiceContract> CaptureCallbackContracts(
        CallbackHandler callbackHandler, bool mouseUserSubroutineRegistered) {
        Dictionary<ushort, ServiceContract> contracts = new();
        foreach (Callback callback in callbackHandler.AllRunnables.OfType<Callback>()) {
            if (mouseUserSubroutineRegistered
                && callback.Index is CallbackNumbers.MouseBeforeUserHandler
                    or CallbackNumbers.MouseAfterUserHandler) {
                continue;
            }
            if (ServiceEntryOf(callback.Runnable) is { } entry) {
                contracts[callback.Index] = entry.Contract;
            }
        }
        return contracts;
    }

    /// <summary>The call instructions every registered callback's target can patch at run time
    /// through an <see cref="IPatchedCallSite"/> (the mouse int 74h driver call and the mouse
    /// driver's user-routine call).</summary>
    private static HashSet<SegmentedAddress> CaptureSwitcherPatchedCalls(CallbackHandler callbackHandler) {
        HashSet<SegmentedAddress> patchedCalls = new();
        foreach (Callback callback in callbackHandler.AllRunnables.OfType<Callback>()) {
            if (callback.Runnable.Target is IPatchedCallSite { PatchedCallAddress: { } address }) {
                patchedCalls.Add(address);
            }
        }
        return patchedCalls;
    }

    /// <summary>The service entry of a callback runnable: the contract of a single-service
    /// handler's <c>Run</c>, or the entry of the method's single selectorless
    /// <see cref="InterruptServiceAttribute"/>; <see langword="null"/> when it is
    /// <see cref="ServiceContract.Unknown"/> or undeclared.</summary>
    internal static InterruptServiceEntry? ServiceEntryOf(Action runnable) {
        if (runnable.Target is IInterruptHandler handler
            && runnable.Method.Name == nameof(InterruptHandler.Run)) {
            return handler.Services is HandlerServices.SingleService single
                && !ReferenceEquals(single.Service.Contract, ServiceContract.Unknown)
                ? single.Service
                : null;
        }
        List<InterruptServiceAttribute> attributes = [.. runnable.Method
            .GetCustomAttributes<InterruptServiceAttribute>()
            .Where(attribute => attribute.SelectorValue is null)];
        if (attributes.Count != 1) {
            return null;
        }
        InterruptServiceEntry entry = attributes[0]
            .ToEntry(runnable.Method.Name, runnable.Method.DeclaringType?.Name ?? "");
        return ReferenceEquals(entry.Contract, ServiceContract.Unknown) ? null : entry;
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
