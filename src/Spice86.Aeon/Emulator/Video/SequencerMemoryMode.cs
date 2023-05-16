namespace Spice86.Aeon.Emulator.Video; 

/// <summary>
/// Represents the memory mode of the VGA Video Sequencer.
/// </summary>
[Flags]
public enum SequencerMemoryMode : byte
{
    /// <summary>
    /// No memory mode selected.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Extended memory mode selected.
    /// </summary>
    ExtendedMemory = 2,
    
    /// <summary>
    /// Odd-even write addressing is disabled.
    /// </summary>
    OddEvenWriteAddressingDisabled = 4,
    
    /// <summary>
    /// Chain-4 mode selected.
    /// </summary>
    Chain4 = 8
}