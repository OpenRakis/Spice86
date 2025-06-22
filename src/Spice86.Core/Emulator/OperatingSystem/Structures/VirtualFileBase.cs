namespace Spice86.Core.Emulator.OperatingSystem.Structures;
using System;
using System.IO;

public abstract class VirtualFileBase : Stream, IVirtualFile {
    public virtual string Name { get; set; } = "";

    /// <summary>
    /// This method is useful for checking if the file name represents a specific unique device name.
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal and case-insensitive.
    /// </remarks>
    /// <param name="name">The filename</param>
    /// <returns>Whether the file name matches the device name.</returns>
    public bool IsName(string name) {
        return !string.IsNullOrWhiteSpace(Name) &&
            Name.Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
