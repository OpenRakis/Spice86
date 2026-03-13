namespace Spice86.ViewModels;

public sealed class EmsHandleEntryViewModel {
    public ushort HandleNumber { get; }
    public string Name { get; }
    public int LogicalPageCount { get; }
    public bool SavedPageMap { get; }

    public EmsHandleEntryViewModel(ushort handleNumber, string name, int logicalPageCount, bool savedPageMap) {
        HandleNumber = handleNumber;
        Name = name;
        LogicalPageCount = logicalPageCount;
        SavedPageMap = savedPageMap;
    }
}
