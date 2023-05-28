namespace Spice86.Core.Emulator.Devices.Video.Registers.Sequencer;

/// <summary>
///     This read/write register has an index of hex 00; its address is hex 03C5.
/// </summary>
public class ResetRegister : Register8 {
    /// <summary>
    ///     When set to 0, the Synchronous Reset field (bit 1) commands the sequencer to synchronously clear and
    ///     halt. Bits 1 and 0 must be 1 to allow the sequencer to operate. To prevent the loss of data, bit 1 must be set
    ///     to 0 during the active display interval before changing the clock selection. The clock is changed through the
    ///     Clocking Mode register or the Miscellaneous Output register.
    /// </summary>
    public bool SynchronousReset {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    ///     When set to 0, the Asynchronous Reset field (bit 0) commands the sequencer to asynchronously clear and
    ///     halt. Resetting the sequencer with this bit can cause loss of video data.
    /// </summary>
    public bool AsynchronousReset {
        get => GetBit(0);
        set => SetBit(0, value);
    }
}