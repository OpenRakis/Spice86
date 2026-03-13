namespace Spice86.ViewModels;

public sealed class EmsLogicalPageEntryViewModel {
    public ushort HandleNumber { get; }
    public int LogicalPageIndex { get; }
    public ushort PageNumber { get; }

    public EmsLogicalPageEntryViewModel(ushort handleNumber, int logicalPageIndex, ushort pageNumber) {
        HandleNumber = handleNumber;
        LogicalPageIndex = logicalPageIndex;
        PageNumber = pageNumber;
    }
}
