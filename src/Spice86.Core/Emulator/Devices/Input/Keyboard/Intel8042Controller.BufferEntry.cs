namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class Intel8042Controller {
    // Controller internal buffer
    private struct BufferEntry {
        public byte Data;
        public bool IsFromAux;
        public bool IsFromKbd;
        public bool SkipDelay;
    }
}