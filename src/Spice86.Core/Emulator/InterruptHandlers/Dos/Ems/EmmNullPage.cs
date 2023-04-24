namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Errors;
/// <summary>
/// An EMM Page set to raise a fatal exception whe the DOS program attempts to read or write into an unmapped physical page
/// </summary>
public class EmmNullPage : IEmmPage, IMemoryDevice {
    public EmmNullPage() {
        PageMemory = this;
    }

    public uint Size => ExpandedMemoryManager.EmmPageSize;
    public byte Read(uint address) {
        throw new UnrecoverableException("The DOS program attempted to read into an unmapped physical EMM page !");
    }

    public void Write(uint address, byte value) {
        throw new UnrecoverableException("The DOS program attempted to write into an unmapped physical EMM page !");
    }

    public Span<byte> GetSpan(int address, int length) {
        throw new UnrecoverableException("Attempt to get a span of an unmapped physical EMM page !");
    }
    public IMemoryDevice PageMemory { get; set; }

    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}