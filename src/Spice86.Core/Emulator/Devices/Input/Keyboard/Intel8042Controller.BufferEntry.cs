namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class Intel8042Controller {
    /// <summary>
    /// Represents a single entry in the microcontroller buffer,
    /// including the data byte and associated source or processing flags.
    /// </summary>
    private struct BufferEntry {
        /// <summary>
        /// The data byte stored in the buffer.
        /// </summary>
        public byte Data;

        /// <summary>
        /// Gets or sets a value indicating whether the data originated from the auxiliary (mouse) device.
        /// </summary>
        public bool IsFromAux;

        /// <summary>
        /// Gets or sets a value indicating whether the data originated from the keyboard.
        /// </summary>
        public bool IsFromKbd;

        /// <summary>
        /// Gets or sets a value indicating whether the standard hardware delay should be skipped for this entry.
        /// This is used for controller-generated responses and for subsequent bytes in a multi-byte packet
        /// to ensure atomic processing.
        /// </summary>
        public bool SkipDelay;
    }
}