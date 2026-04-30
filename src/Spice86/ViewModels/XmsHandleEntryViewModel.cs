namespace Spice86.ViewModels;

public sealed class XmsHandleEntryViewModel {
    public int Handle { get; }
    public byte LockCount { get; }

    public XmsHandleEntryViewModel(int handle, byte lockCount) {
        Handle = handle;
        LockCount = lockCount;
    }
}
