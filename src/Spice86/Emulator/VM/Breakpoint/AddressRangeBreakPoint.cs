namespace Spice86.Emulator.VM.Breakpoint;

using JetBrains.Annotations;

public class AddressRangeBreakPoint : BreakPoint {
    public long StartAddress { get; private set; }
    public long EndAddress { get; private set; }

    public AddressRangeBreakPoint(BreakPointType breakPointType, long startAddress, long endAddress, [NotNull] [ItemNotNull] Action<BreakPoint> onReached, bool isRemovedOnTrigger) : base(breakPointType, onReached, isRemovedOnTrigger) {
        this.StartAddress = startAddress;
        this.EndAddress = endAddress;
    }

    public override bool Matches(long address) {
        return StartAddress <= address && EndAddress >= address;
    }

    public override bool Matches(long startAddress, long endAddress) {
        return startAddress <= EndAddress && endAddress >= StartAddress;
    }
}