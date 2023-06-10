namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Provides access to emulated memory mapped BIOS values.
/// </summary>
public sealed class BiosDataArea {
    private readonly Memory _memory;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public BiosDataArea(Memory memory) {
        _memory = memory;
    }
    
    /// <summary>
    /// Gets or sets the flags that indicate which hardware is installed.
    /// </summary>
    public ushort EquipmentListFlags {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0010];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0010] = value;
    }

    /// <summary>
    /// Gets or sets the value of the disk motor timer.
    /// </summary>
    public byte DiskMotorTimer {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0040];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0040] = value;
    }

    /// <summary>
    /// Gets or sets the BIOS video mode.
    /// </summary>
    public byte VideoMode {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0049];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0049] = value;
    }

    /// <summary>
    /// Gets or sets the BIOS screen column count.
    /// </summary>
    public ushort ScreenColumns {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004A];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004A] = value;
    }
    
    /// <summary>
    /// Gets or sets the size of active video page in bytes.
    /// </summary>
    public ushort VideoPageSize {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004C];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004C] = value;
    }
    
    /// <summary>
    /// Gets or sets the offset address of the active video page relative to the start of video RAM
    /// </summary>
    public ushort VideoPageStart {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004E];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x004E] = value;
    }

    /// <summary>
    /// Gets the word representing the cursor position on the specified text page.
    /// </summary>
    public ushort GetCursorPosition(int page) {
        return _memory.UInt16[MemoryMap.BiosDataSegment, (ushort)(0x0050 + page * 2)];
    }

    /// <summary>
    /// Sets the word representing the cursor position on the specified text page.
    /// </summary>
    public void SetCursorPosition(int page, ushort value) {
        _memory.UInt16[MemoryMap.BiosDataSegment, (ushort)(0x0050 + page * 2)] = value;
    }

    /// <summary>
    /// Gets or sets the BIOS cursor type.
    /// </summary>
    public ushort CursorType {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0060];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0060] = value;
    }

    /// <summary>
    /// Gets or sets the currently active video page.
    /// </summary>
    public byte CurrentVideoPage {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0062];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0062] = value;
    }
    
    /// <summary>
    /// Gets or sets the CRT controller I/O port address.
    /// </summary>
    public ushort CrtControllerBaseAddress {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0063];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0063] = value;
    }

    /// <summary>
    /// Gets or sets the current value of the real time clock.
    /// </summary>
    public uint RealTimeClock {
        get => _memory.UInt32[MemoryMap.BiosDataSegment, 0x006C];
        set => _memory.UInt32[MemoryMap.BiosDataSegment, 0x006C] = value;
    }

    /// <summary>
    /// Gets or sets the screen row count.
    /// </summary>
    public byte ScreenRows {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0084];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0084] = value;
    }

    /// <summary>
    /// Gets or sets the character point height.
    /// </summary>
    public ushort CharacterHeight {
        get => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0085];
        set => _memory.UInt16[MemoryMap.BiosDataSegment, 0x0085] = value;
    }
    
    /// <summary>
    /// Gets or sets the VideoCtl.
    /// </summary>
    public byte VideoCtl {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0087];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0087] = value;
    }

    /// <summary>
    /// Gets or sets the BIOS video mode options.
    /// </summary>
    public byte FeatureSwitches {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0088];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0088] = value;
    }

    /// <summary>
    /// Gets or sets the EGA feature switch values.
    /// </summary>
    public byte ModesetCtl {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0089];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x0089] = value;
    }
    
    /// <summary>
    /// Gets or sets the display combination code.
    /// </summary>
    public byte DisplayCombinationCode {
        get => _memory.UInt8[MemoryMap.BiosDataSegment, 0x008A];
        set => _memory.UInt8[MemoryMap.BiosDataSegment, 0x008A] = value;
    }
}