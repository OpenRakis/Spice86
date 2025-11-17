namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class Intel8042Controller {
    /// <summary>
    /// Common controller byte values used across 8042 operations.
    /// </summary>
    public enum Response : byte {
        /// <summary>
        /// Generic OK/zero value, used by port tests and various default reads.
        /// Also used as NUL terminator for the firmware copyright string.
        /// </summary>
        Ok = 0x00,
        /// <summary>
        /// Controller self-test passed value (result of command 0xAA).
        /// </summary>
        SelfTestPassed = 0x55,
        /// <summary>
        /// Password not installed or unsupported (command 0xA4 result).
        /// </summary>
        PasswordNotInstalled = 0xF1,
        /// <summary>
        /// Space key make code in Set 1 (used by diagnostic dump formatting).
        /// </summary>
        SpaceScanCodeSet1 = 0x39,
    }
}