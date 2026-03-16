namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class PS2Keyboard {
    /// <summary>
    /// Represents the configuration data for a typematic (key repeat) event, including the key involved and timing
    /// parameters.
    /// </summary>
    private struct RepeatData {
        /// <summary>
        /// Key repeated while it is pressed.
        /// </summary>
        public PcKeyboardKey Key;
        /// <summary>
        /// The number of milliseconds to wait before the next event occurs.
        /// </summary>
        public int WaitMs;
        /// <summary>
        /// The initial pause duration, in milliseconds, before starting the repeat.
        /// </summary>
        public int PauseMs;
        /// <summary>
        /// Specifies the repeat rate, in milliseconds.
        /// </summary>
        public int RateMs;
    }
}