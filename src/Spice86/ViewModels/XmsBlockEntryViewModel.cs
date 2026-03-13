namespace Spice86.ViewModels;

public sealed class XmsBlockEntryViewModel {
    public bool IsFree { get; }
    public int Handle { get; }
    public uint Offset { get; }
    public uint Length { get; }

    public XmsBlockEntryViewModel(bool isFree, int handle, uint offset, uint length) {
        IsFree = isFree;
        Handle = handle;
        Offset = offset;
        Length = length;
    }

    public uint EndOffset => Offset + Length;
    public uint LengthInKb => Length / 1024;
    public string State => IsFree ? "Free" : "Allocated";
}
