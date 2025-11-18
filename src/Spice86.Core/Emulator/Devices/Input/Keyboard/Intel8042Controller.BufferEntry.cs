namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class Intel8042Controller {
    /// <summary>
    /// Represents a single entry in the microcontroller buffer,
    /// including the data byte and associated source or processing flags.
    /// </summary>
    private struct BufferEntry {
        public byte Data;
        public bool IsFromAux;
        public bool IsFromKbd;
        public bool SkipDelay;
    }
}