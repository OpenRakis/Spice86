namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// The emulator-provided code of one run, captured on the fresh machine before the program starts.
/// The emulator registers every callback it writes to memory, and the bytes of that callback instruction
/// are known to the emulator itself: <c>FE 38</c> followed by the callback index. A registered site whose
/// memory still holds those bytes is emulator code by construction, and the asm glue the emulator
/// installed for that handler sits within <see cref="GlueWindowBytes"/> of it. Everything else the
/// interpreter executes is the test program's own code, whatever address, segment or fixture it comes
/// from; an address the emulator never wrote is not trusted.
/// Content decides ownership, so a program image that occupies or overwrites an emulator location loses
/// that location's exemption: the site no longer holds the emulator's callback bytes, and any instruction
/// executed on overwritten memory has the program's own bytes, not the ones recorded here before the
/// program started. The inventory holds plain data only, never a machine reference, so a rejected run
/// stays collectible.
/// </summary>
internal sealed class EmulatorCodeInventory {
    /// <summary>First byte of the callback instruction the emulator writes.</summary>
    private const byte CallbackOpcodeFirstByte = 0xFE;

    /// <summary>Second byte of the callback instruction the emulator writes.</summary>
    private const byte CallbackOpcodeSecondByte = 0x38;

    /// <summary>Length in bytes of the callback instruction the emulator writes.</summary>
    private const int CallbackInstructionBytes = 4;

    /// <summary>How far an emulator asm instruction can sit from the callback instruction of its handler.
    /// Emulator handlers are short sequences written around their callbacks.</summary>
    private const int GlueWindowBytes = 64;

    /// <summary>Longest x86 instruction, so the recorded and snapshotted bytes always cover a whole
    /// instruction that starts at the edge of a glue window.</summary>
    private const int MaxInstructionBytes = 15;

    private readonly List<EmulatorCallbackSite> _callbackSites;

    private EmulatorCodeInventory(List<EmulatorCallbackSite> callbackSites) {
        _callbackSites = callbackSites;
    }

    /// <summary>Captures the emulator code of a fresh machine, before its program starts executing.</summary>
    /// <param name="machine">Machine whose registered callbacks are captured.</param>
    /// <returns>The emulator code inventory of that machine.</returns>
    public static EmulatorCodeInventory Capture(Machine machine) {
        IMemory memory = machine.Memory;
        List<EmulatorCallbackSite> callbackSites = new();
        foreach (ICallback callback in machine.CallbackHandler.AllRunnables) {
            uint siteAddress = LinearAddressOf(callback.InstructionAddress);
            byte[] callbackInstruction = CallbackInstructionOf(callback.Index);
            if (!ReadBytes(memory, siteAddress, callbackInstruction.Length).SequenceEqual(callbackInstruction)) {
                continue;
            }
            uint snapshotStart = siteAddress >= GlueWindowBytes ? siteAddress - GlueWindowBytes : 0;
            uint snapshotEnd = siteAddress + GlueWindowBytes + MaxInstructionBytes;
            callbackSites.Add(new EmulatorCallbackSite(siteAddress, snapshotStart,
                ReadBytes(memory, snapshotStart, (int)(snapshotEnd - snapshotStart))));
        }
        return new EmulatorCodeInventory(callbackSites);
    }

    /// <summary>Whether the given instruction is emulator-provided code rather than program code.</summary>
    /// <param name="address">Address the interpreter was about to execute.</param>
    /// <param name="instructionBytes">Bytes of that instruction as they were when it was about to run.</param>
    /// <returns><c>true</c> when the instruction is emulator asm glue of a registered callback.</returns>
    public bool IsEmulatorProvided(SegmentedAddress address, byte[] instructionBytes) {
        if (instructionBytes.Length == 0) {
            return false;
        }
        uint linearAddress = LinearAddressOf(address);
        foreach (EmulatorCallbackSite callbackSite in _callbackSites) {
            if (!IsWithinGlueWindow(linearAddress, callbackSite.LinearAddress)) {
                continue;
            }
            if (MatchesSnapshot(callbackSite, linearAddress, instructionBytes)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>The instruction bytes the emulator writes for a callback, which identify its site.</summary>
    /// <param name="callbackIndex">Index the callback is registered under.</param>
    /// <returns>The four bytes of that callback instruction.</returns>
    private static byte[] CallbackInstructionOf(ushort callbackIndex) {
        return [
            CallbackOpcodeFirstByte,
            CallbackOpcodeSecondByte,
            (byte)(callbackIndex & 0xFF),
            (byte)(callbackIndex >> 8)
        ];
    }

    private static bool IsWithinGlueWindow(uint linearAddress, uint callbackSiteAddress) {
        long distance = (long)linearAddress - callbackSiteAddress;
        return distance >= -GlueWindowBytes && distance <= GlueWindowBytes;
    }

    private static bool MatchesSnapshot(EmulatorCallbackSite callbackSite, uint linearAddress, byte[] instructionBytes) {
        int snapshotOffset = (int)(linearAddress - callbackSite.SnapshotStart);
        if (snapshotOffset < 0 || snapshotOffset + instructionBytes.Length > callbackSite.Snapshot.Length) {
            return false;
        }
        for (int i = 0; i < instructionBytes.Length; i++) {
            if (callbackSite.Snapshot[snapshotOffset + i] != instructionBytes[i]) {
                return false;
            }
        }
        return true;
    }

    private static uint LinearAddressOf(SegmentedAddress address) {
        return MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
    }

    private static byte[] ReadBytes(IMemory memory, uint linearAddress, int length) {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++) {
            bytes[i] = memory.SneakilyRead(linearAddress + (uint)i);
        }
        return bytes;
    }

    /// <summary>One registered callback site, with the memory snapshot of the glue installed around it.</summary>
    /// <param name="LinearAddress">Linear address of the callback instruction.</param>
    /// <param name="SnapshotStart">Linear address the snapshot starts at.</param>
    /// <param name="Snapshot">Memory content around the callback instruction, as it was before the run.</param>
    private sealed record EmulatorCallbackSite(uint LinearAddress, uint SnapshotStart, byte[] Snapshot);
}
