namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class Intel8042Controller {
    /// <summary>
    /// Controller mode values.
    /// </summary>
    public enum ControllerModeValue : byte {
        /// <summary>
        /// ISA (AT) mode.
        /// </summary>
        IsaAt = 0x00,
        /// <summary>
        /// PS/2 (MCA) mode.
        /// </summary>
        Ps2Mca = 0x01,
    }
}