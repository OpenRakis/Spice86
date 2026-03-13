namespace Spice86.ViewModels;

public sealed class EmsPhysicalPageEntryViewModel {
    public ushort PhysicalPageNumber { get; }
    public string FrameAddress { get; }
    public string HandleNumber { get; }
    public string LogicalPageNumber { get; }

    public EmsPhysicalPageEntryViewModel(ushort physicalPageNumber, string frameAddress, string handleNumber, string logicalPageNumber) {
        PhysicalPageNumber = physicalPageNumber;
        FrameAddress = frameAddress;
        HandleNumber = handleNumber;
        LogicalPageNumber = logicalPageNumber;
    }
}
