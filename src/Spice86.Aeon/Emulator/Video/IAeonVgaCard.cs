namespace Spice86.Aeon.Emulator.Video;

using Spice86.Aeon.Emulator.Video.Modes;

/// <summary>
/// The VGA card interface, lifted from the Aeon DOS emulator.
/// </summary>
public interface IAeonVgaCard {
    /// <summary>
    /// Gets the VGA DAC.
    /// </summary>
    public Dac Dac { get; }

    /// <summary>
    /// Gets the VGA graphics controller.
    /// </summary>
    public Graphics Graphics { get; }

    /// <summary>
    /// Gets the VGA sequencer.
    /// </summary>
    public Sequencer Sequencer { get; }

    /// <summary>
    /// Gets the VGA CRT controller.
    /// </summary>
    public CrtController CrtController { get; }

    /// <summary>
    /// Gets the current display mode.
    /// </summary>
    public VideoMode CurrentMode { get; }

    /// <summary>
    /// Gets the VGA attribute controller.
    /// </summary>
    public AttributeController AttributeController { get; }
    
    /// <summary>
    /// Gets a pointer to the emulated video RAM.
    /// </summary>
    public IntPtr VideoRam { get; }
    
    /// <summary>
    /// Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }

    /// <summary>
    /// Total number of bytes allocated for video RAM.
    /// </summary>
    public uint TotalVramBytes { get; }

}