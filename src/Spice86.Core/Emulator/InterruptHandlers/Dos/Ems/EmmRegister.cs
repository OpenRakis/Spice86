namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A representation of an EMM Register. <br/>
/// Enables the mapping of a logical page into main memory.
/// </summary>
public class EmmRegister : IMemoryDevice {
    /// <summary>
    /// The physical page mapped in main memory, set to a logical page reference.
    /// </summary>
    public EmmPage PhysicalPage { get; set; }

    /// <summary>
    /// The start address of the register, in main memory. <br/>
    /// This is an absolute address within the EMM Page Frame.
    /// </summary>
    public uint Offset { get; init; }

    /// <summary>
    /// Constructs a new instance.
    /// </summary>
    /// <param name="logicalPage">The logical page that will be mapped.</param>
    /// <param name="offset">The start address of the register, in main memory.</param>
    public EmmRegister(EmmPage logicalPage, uint offset) {
        PhysicalPage = logicalPage;
        Offset = offset;
    }

    /// <inheritdoc />
    public uint Size => ExpandedMemoryManager.EmmPageSize;

    /// <inheritdoc />
    public byte Read(uint address) {
        return PhysicalPage.Read(address - Offset);
    }

    /// <inheritdoc />
    public void Write(uint address, byte value) {
        PhysicalPage.Write(address - Offset, value);
    }

    /// <inheritdoc />
    public IList<byte> GetSlice(int address, int length) {
        return PhysicalPage.GetSlice((int)(address - Offset), length);
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<byte> span, MemoryAccess access) {
        if (length >= 0) {
            long pageAddress = (long)startAddress - Offset;
            if (pageAddress >= 0 && length <= ExpandedMemoryManager.EmmPageSize - pageAddress) {
                return PhysicalPage.TryGetSpan((uint)pageAddress, length, out span, access);
            }
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<byte> span, MemoryAccess access) {
        if (length >= 0) {
            long pageAddress = (long)startAddress - Offset;
            if (pageAddress >= 0 && length <= ExpandedMemoryManager.EmmPageSize - pageAddress) {
                return PhysicalPage.TryGetSpan((uint)pageAddress, length, out span, access);
            }
        }

        span = [];
        return false;
    }
}