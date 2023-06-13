namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Provides access to emulated memory mapped BIOS values.
/// </summary>
public sealed class ExtendedBiosDataArea {
    private readonly Memory _memory;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public ExtendedBiosDataArea(Memory memory) {
        _memory = memory;
    }

    /// <summary>
    /// Size
    /// </summary>
    public ushort Size => 0x01;

    /// <summary>
    /// Gets or sets the offset of the far call pointer that is used to handle mouse interrupts.
    /// </summary>
    public ushort FarCallPointerOffset {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0022];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0022] = value;
    }

    /// <summary>
    /// Gets or sets the segment of the far call pointer that is used to handle mouse interrupts.
    /// </summary>
    public ushort FarCallPointerSegment {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0024];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0024] = value;
    }

    /// <summary>
    /// Gets or sets first mouse flag
    /// </summary>
    public byte MouseFlag1 {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0026];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0026] = value;
    }

    /// <summary>
    /// Gets or sets second mouse flag
    /// </summary>
    public byte MouseFlag2 {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0027];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0027] = value;
    }

    /// <summary>
    /// Gets or sets the mouse status flags.
    /// </summary>
    public ushort MouseStatus {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0028];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0028] = value;
    }

    /// <summary>
    /// Gets or sets the mouse X value.
    /// </summary>
    public ushort MouseX {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0028];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0028] = value;
    }

    /// <summary>
    /// Gets or sets the mouse Y value.
    /// </summary>
    public ushort MouseY {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x002A];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x002A] = value;
    }
}