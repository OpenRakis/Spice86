namespace Spice86.Core.Emulator.OperatingSystem.Structures;
public interface IVirtualFile {
    public string Name { get; set; }

    public ushort Information { get; }
}
