namespace Spice86.Emulator.VM.Breakpoint;

using JetBrains.Annotations;

public class AddressBreakPoint : BreakPoint {
    public long Address { get; private set; }
    public AddressBreakPoint(BreakPointType breakPointType, long address, [NotNull] [ItemNotNull] Action<BreakPoint> onReached, bool isRemovedOnTrigger) : base(breakPointType, onReached, isRemovedOnTrigger) {
        this.Address = address;
    }

    public override bool Matches(long address) {
        return Address == address;
    }

    public override bool Matches(long startAddress, long endAddress) {
        return Address >= startAddress && this.Address < endAddress;
    }
}