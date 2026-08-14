namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Shared GDT/LDT segment descriptor decoding, used both by the protected-mode MMU (which only has a
/// raw byte reader available) and by segment-load instruction execution (which has full memory
/// access). Reused instead of duplicated so table-limit and selector-decoding logic has one home.
/// </summary>
public static class DescriptorTableReader {
    /// <summary>
    /// Decodes the segment descriptor for <paramref name="selector"/> from the GDT or LDT, depending
    /// on the selector's table indicator bit.
    /// </summary>
    /// <param name="selector">The raw selector value being resolved.</param>
    /// <param name="gdtBase">The linear base address of the GDT.</param>
    /// <param name="gdtLimit">The byte limit of the GDT.</param>
    /// <param name="ldtBase">The linear base address of the currently loaded LDT.</param>
    /// <param name="ldtLimit">The byte limit of the currently loaded LDT.</param>
    /// <param name="readByte">Reads one linear/physical byte of memory (no MMU translation applied).</param>
    /// <exception cref="CpuGeneralProtectionFaultException">The selector is null or outside its table's limit.</exception>
    public static SegmentDescriptorCache ReadDescriptor(ushort selector, uint gdtBase, uint gdtLimit,
        uint ldtBase, uint ldtLimit, Func<uint, byte> readByte) {
        if (!TryReadDescriptor(selector, gdtBase, gdtLimit, ldtBase, ldtLimit, readByte, out SegmentDescriptorCache descriptor)) {
            throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4} is outside its descriptor table limit");
        }
        return descriptor;
    }

    /// <summary>
    /// Attempts to decode the segment descriptor for <paramref name="selector"/>, without faulting.
    /// Used by LAR/LSL/VERR/VERW, which report an invalid selector via the zero flag rather than an
    /// exception.
    /// </summary>
    /// <returns><c>false</c> if the selector is null or outside its descriptor table's limit.</returns>
    public static bool TryReadDescriptor(ushort selector, uint gdtBase, uint gdtLimit,
        uint ldtBase, uint ldtLimit, Func<uint, byte> readByte, out SegmentDescriptorCache descriptor) {
        SegmentSelector segmentSelector = new(selector);
        uint tableBase = segmentSelector.ReferencesLocalDescriptorTable ? ldtBase : gdtBase;
        uint tableLimit = segmentSelector.ReferencesLocalDescriptorTable ? ldtLimit : gdtLimit;
        uint entryOffset = (uint)segmentSelector.Index * 8u;
        if (segmentSelector.IsNull || entryOffset + 7u > tableLimit) {
            descriptor = default;
            return false;
        }

        Span<byte> descriptorBytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++) {
            descriptorBytes[i] = readByte(tableBase + entryOffset + (uint)i);
        }
        descriptor = new RawSegmentDescriptor(descriptorBytes).ToSegmentDescriptorCache();
        return true;
    }
}
