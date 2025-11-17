namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using System.Diagnostics;

public partial class Intel8042Controller {
    /// <summary>
    /// Byte 0x00 of the controller memory - configuration byte
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public sealed class ConfigByte {
        public ConfigByte(byte initial = 0) => FromByte(initial);

        // bit0: keyboard IRQ enabled (IRQ1)
        public bool KbdIrqEnabled { get; set; }
        // bit1: mouse IRQ enabled (IRQ12)
        public bool MouseIrqEnabled { get; set; }
        // bit2: self-test passed
        public bool SelfTestPassed { get; set; }
        // bit3: reserved
        public bool Reserved3 { get; set; }
        // bit4: keyboard port disabled
        public bool IsKeyboardPortDisabled { get; set; }
        // bit5: aux (mouse) port disabled
        public bool IsAuxPortDisabled { get; set; }
        // bit6: translation enabled (AT -> XT)
        public bool TranslationEnabled { get; set; }
        // bit7: reserved
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