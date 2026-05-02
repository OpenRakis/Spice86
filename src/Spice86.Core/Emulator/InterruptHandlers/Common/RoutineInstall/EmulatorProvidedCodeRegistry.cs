namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Tracks all ASM routines that the emulator itself wrote into emulated memory (interrupt
/// handlers, the mouse driver, and any other "trampoline" produced via <c>MemoryAsmWriter</c>).
/// <para>
/// Populated by <see cref="AssemblyRoutineInstaller"/> as a side effect of each install. Used
/// by the debugger UI to draw a visual border around the corresponding instructions in the
/// disassembly and CFG views, marking them as "not part of the emulated program".
/// </para>
/// </summary>
public sealed class EmulatorProvidedCodeRegistry {
    private readonly List<ProvidedRoutineInfo> _routines = new();

    /// <summary>
    /// All registered routines, in the order they were installed.
    /// </summary>
    public IReadOnlyList<ProvidedRoutineInfo> Routines => _routines;

    /// <summary>
    /// Adds a new routine to the registry.
    /// </summary>
    /// <param name="info">Routine information.</param>
    public void Register(ProvidedRoutineInfo info) {
        _routines.Add(info);
    }

    /// <summary>
    /// Returns true when the given address falls within any registered routine.
    /// </summary>
    /// <param name="address">Address to test.</param>
    public bool IsEmulatorProvided(SegmentedAddress address) {
        return TryGet(address, out _);
    }

    /// <summary>
    /// Returns the routine the given address belongs to, if any.
    /// </summary>
    /// <param name="address">Address to test.</param>
    /// <param name="info">Matching routine when the method returns true; null otherwise.</param>
    public bool TryGet(SegmentedAddress address, [NotNullWhen(true)] out ProvidedRoutineInfo? info) {
        uint physical = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        foreach (ProvidedRoutineInfo routine in _routines) {
            uint start = MemoryUtils.ToPhysicalAddress(routine.Start.Segment, routine.Start.Offset);
            uint end = start + (uint)routine.ByteLength;
            if (physical >= start && physical < end) {
                info = routine;
                return true;
            }
        }
        info = null;
        return false;
    }
}
