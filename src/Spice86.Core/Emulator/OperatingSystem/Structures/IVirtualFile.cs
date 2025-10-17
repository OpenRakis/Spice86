namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.ComponentModel.DataAnnotations;

public interface IVirtualFile {
    /// <summary>
    /// The DOS file name of the file or device.
    /// </summary>
    [Range(0, 13)]
    public string Name { get; set; }
}
