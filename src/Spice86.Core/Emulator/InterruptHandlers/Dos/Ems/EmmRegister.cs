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
    
    public EmmRegister(EmmPage logicalPage, uint offset) {
        PhysicalPage = logicalPage;
        Offset = offset;
    }

    public uint Size => ExpandedMemoryManager.EmmPageSize;
    
    public byte Read(uint address) {
        return PhysicalPage.PageMemory.Read(address - Offset);
    }

    public void Write(uint address, byte value) {
        PhysicalPage.PageMemory.Write(address - Offset, value);
    }

    public Span<byte> GetSpan(int address, int length) {
        return PhysicalPage.PageMemory.GetSpan((int)(address - Offset), length);
    }
}