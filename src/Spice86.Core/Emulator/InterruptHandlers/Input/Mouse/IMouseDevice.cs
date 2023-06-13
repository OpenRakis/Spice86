namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;

public interface IMouseDevice : IIOPortHandler {
    MouseType MouseType { get; }

    /// <summary>
    /// Horizontal mouse position as a ratio of the screen width.
    /// </summary>
    double MouseXRelative { get; }
    /// <summary>
    /// Vertical mouse position as a ratio of the screen height.
    /// </summary>
    double MouseYRelative { get; }
    bool IsLeftButtonDown { get; }
    bool IsRightButtonDown { get; }
    bool IsMiddleButtonDown { get; }
    ushort DoubleSpeedThreshold { get; set; }
    ushort HorizontalMickeysPerPixel { get; set; }
    ushort VerticalMickeysPerPixel { get; set; }
    MouseEventMask LastTrigger { get; }
    ushort SampleRate { get; set; }
}