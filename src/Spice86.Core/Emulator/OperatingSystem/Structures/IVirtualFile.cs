namespace Spice86.Core.Emulator.OperatingSystem.Structures;

public interface IVirtualFile {
    /// <summary>
    /// The DOS file name of the file or device.
    /// </summary>
    public string Name { get; set; }
}