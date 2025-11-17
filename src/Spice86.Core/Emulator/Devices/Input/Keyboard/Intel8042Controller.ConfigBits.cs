namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU;

using System;

public partial class Intel8042Controller {
    /// <summary>
    /// Configuration byte bit definitions.
    /// </summary>
    [Flags]
    public enum ConfigBits : byte {
        /// <summary>
        /// Bit 0: Keyboard IRQ enabled (IRQ1).
        /// </summary>
        KbdIrqEnabled = 1 << 0,
        /// <summary>
        /// Bit 1: Mouse IRQ enabled (IRQ12).
        /// </summary>
        MouseIrqEnabled = 1 << 1,
        /// <summary>
        /// Bit 2: Self-test passed.
        /// </summary>
        SelfTestPassed = 1 << 2,
        /// <summary>
        /// Bit 3: Reserved.
        /// </summary>
        Reserved3 = 1 << 3,
        /// <summary>
        /// Bit 4: Keyboard port disabled.
        /// </summary>
        DisableKbdPort = 1 << 4,
        /// <summary>
        /// Bit 5: Auxiliary (mouse) port disabled.
        /// </summary>
        DisableAuxPort = 1 << 5,
        /// <summary>
        /// Bit 6: Translation enabled (AT -&gt; XT).
        /// </summary>
        TranslationEnabled = 1 << 6,
        /// <summary>
        /// Bit 7: Reserved.
        /// </summary>
        Reserved7 = 1 << 7,
    }
}