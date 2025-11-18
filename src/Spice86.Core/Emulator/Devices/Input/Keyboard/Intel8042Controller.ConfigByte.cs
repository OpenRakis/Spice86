namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using System.Diagnostics;

public partial class Intel8042Controller {
    /// <summary>
    /// Byte 0x00 of the controller memory - configuration byte
    /// </summary>
    /// <remarks>This has been converted into a class for easier debugging and implementation.</remarks>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public sealed class ConfigByte {
        public ConfigByte(byte initial = 0) => FromByte(initial);

        /// <summary>
        /// bit0: keyboard IRQ enabled (IRQ1)
        /// </summary>
        /// <remarks>
        /// Read when activating IRQs and by the <see cref="KeyboardCommand.ReadOutputPort"/> (0xD0) command.
        /// Written by the <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) command.
        /// </remarks>
        public bool KbdIrqEnabled { get; set; }
        /// <summary>
        /// bit1: mouse IRQ enabled (IRQ12)
        /// </summary>
        /// <remarks>
        /// Read when activating IRQs and by the <see cref="KeyboardCommand.ReadOutputPort"/> (0xD0) command.
        /// Written by the <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) command.
        /// </remarks>
        public bool MouseIrqEnabled { get; set; }
        /// <summary>
        /// bit2: self-test passed
        /// </summary>
        /// <remarks>
        /// Written by the <see cref="KeyboardCommand.TestController"/> (0xAA) and <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) commands.
        /// </remarks>
        public bool SelfTestPassed { get; set; }
        /// <summary>
        /// bit3: reserved
        /// </summary>
        /// <remarks>
        /// This bit is forced to 0 when the configuration byte is written via the <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) command.
        /// </remarks>
        public bool Reserved3 { get; set; }
        /// <summary>
        /// bit4: keyboard port disabled
        /// </summary>
        /// <remarks>
        /// Read when data is added to the buffer from the keyboard.
        /// Written by the <see cref="KeyboardCommand.DisablePortKbd"/> (0xAD),
        /// <see cref="KeyboardCommand.EnableKeyboardPort"/> (0xAE),
        /// <see cref="KeyboardCommand.TestController"/> (0xAA),
        /// <see cref="KeyboardCommand.TestPortKbd"/> (0xAB), and
        /// <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) commands.
        /// Also written when data is sent to the keyboard.
        /// </remarks>
        public bool IsKeyboardPortDisabled { get; set; }
        /// <summary>
        /// bit5: aux (mouse) port disabled
        /// </summary>
        /// <remarks>
        /// Read when data is added to the buffer from the auxiliary device.
        /// Written by the <see cref="KeyboardCommand.DisablePortAux"/> (0xA7),
        /// <see cref="KeyboardCommand.EnablePortAux"/> (0xA8),
        /// <see cref="KeyboardCommand.TestController"/> (0xAA),
        /// <see cref="KeyboardCommand.TestPortAux"/> (0xA9), and
        /// <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) commands.
        /// </remarks>
        public bool IsAuxPortDisabled { get; set; }
        /// <summary>
        /// bit6: translation enabled (AT -> XT)
        /// </summary>
        /// <remarks>
        /// Read when keyboard data is added to the buffer to determine if scancode translation is needed.
        /// Written by the <see cref="KeyboardCommand.TestController"/>
        /// (0xAA) and <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) commands.
        /// </remarks>
        public bool TranslationEnabled { get; set; }
        /// <summary>
        /// bit7: reserved
        /// </summary>
        /// <remarks>
        /// This bit is forced to 0 when the configuration byte is written via the <see cref="KeyboardCommand.WriteByteConfig"/> (0x60) command.
        /// </remarks>
        public bool Reserved7 { get; set; }

        public byte ToByte() {
            byte v = 0;
            if (KbdIrqEnabled) v |= (byte)ConfigBits.KbdIrqEnabled;
            if (MouseIrqEnabled) v |= (byte)ConfigBits.MouseIrqEnabled;
            if (SelfTestPassed) v |= (byte)ConfigBits.SelfTestPassed;
            if (Reserved3) v |= (byte)ConfigBits.Reserved3;
            if (IsKeyboardPortDisabled) v |= (byte)ConfigBits.DisableKbdPort;
            if (IsAuxPortDisabled) v |= (byte)ConfigBits.DisableAuxPort;
            if (TranslationEnabled) v |= (byte)ConfigBits.TranslationEnabled;
            if (Reserved7) v |= (byte)ConfigBits.Reserved7;
            return v;
        }

        public void FromByte(byte value) {
            KbdIrqEnabled = (value & (byte)ConfigBits.KbdIrqEnabled) != 0;
            MouseIrqEnabled = (value & (byte)ConfigBits.MouseIrqEnabled) != 0;
            SelfTestPassed = (value & (byte)ConfigBits.SelfTestPassed) != 0;
            Reserved3 = (value & (byte)ConfigBits.Reserved3) != 0;
            IsKeyboardPortDisabled = (value & (byte)ConfigBits.DisableKbdPort) != 0;
            IsAuxPortDisabled = (value & (byte)ConfigBits.DisableAuxPort) != 0;
            TranslationEnabled = (value & (byte)ConfigBits.TranslationEnabled) != 0;
            Reserved7 = (value & (byte)ConfigBits.Reserved7) != 0;
        }

        private string DebuggerDisplay =>
            $"0x{ToByte():X2} (IRQ1={KbdIrqEnabled}, IRQ12={MouseIrqEnabled}, KbdDis={IsKeyboardPortDisabled}, AuxDis={IsAuxPortDisabled}, Xlate={TranslationEnabled}, SelfTest={SelfTestPassed})";
    }
}