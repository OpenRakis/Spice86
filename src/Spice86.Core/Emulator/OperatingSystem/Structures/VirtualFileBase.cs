namespace Spice86.Core.Emulator.OperatingSystem.Structures;
using System;
using System.IO;

public abstract class VirtualFileBase : Stream, IVirtualFile {
    public virtual string Name { get; set; } = "";
    public abstract ushort Information { get; }

    public bool IsName(string name) {
        return !string.IsNullOrWhiteSpace(Name) &&
            Name.Equals(name, StringComparison.Ordinal);
    }
}
