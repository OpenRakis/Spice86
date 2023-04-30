namespace Spice86.Core.Emulator.Devices.Video.Registers.Sequencer;

public class MemoryModeRegister : Register8 {
    /// <summary>
    ///     When set to 1, the Extended Memory field (bit 1) enables the video memory from 64KB to 256KB. This
    ///     bit must be set to 1 to enable the character map selection.
    /// </summary>
    public bool ExtendedMemory {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    ///     When the Odd/Even field (bit 2) is set to 0, even system addresses access maps 0 and 2, while odd system
    ///     addresses access maps 1 and 3. When set to 1, system addresses sequentially access data within a bit map,
    ///     and the maps are accessed according to the value in the Map Mask register (hex 02).
    /// </summary>
    public bool OddEvenMode {
        get => !GetBit(2);
        set => SetBit(2, !value);
    }

    /// <summary>
    ///     The Chain 4 field (bit 3) controls the map selected during system read operations. When set to 0, this bit
    ///     enables system addresses to sequentially access data within a bit map by using the Map Mask register. When
    ///     set to 1, this bit causes the 2 low-order bits to select the map accessed
    /// </summary>
    public bool Chain4Mode {
        get => GetBit(3);
        set => SetBit(3, value);
    }
}