namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Shared.Interfaces;

/// <summary>
/// Contains the keyboard, mouse, and joystick.
/// <remarks>BIOS related interrupt handlers live in this subsystem.</remarks>
/// </summary>
public class InputSubsystem {
    /// <summary>
    /// INT9H handler.
    /// </summary>
    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }
    
    /// <summary>
    /// A gameport joystick
    /// </summary>
    public Joystick Joystick { get; }

    /// <summary>
    /// An IBM PC Keyboard
    /// </summary>
    public Keyboard Keyboard { get; }

    /// <summary>
    /// INT16H handler.
    /// </summary>
    public KeyboardInt16Handler KeyboardInt16Handler { get; }
    

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine</param>
    /// <param name="gui">The emulator UI. Can be null in headless mode.</param>
    /// <param name="configuration">The emulator configuration</param>
    /// <param name="loggerService">The logger implementation</param>
    public InputSubsystem(Machine machine, IMainWindowViewModel? gui, Configuration configuration, ILoggerService loggerService) {
        Keyboard = new Keyboard(machine, loggerService, gui, configuration);
        machine.RegisterIoPortHandler(Keyboard);
        Joystick = new(machine, configuration, loggerService);
        machine.RegisterIoPortHandler(Joystick);
        BiosKeyboardInt9Handler = new(machine, Keyboard, loggerService);
        machine.RegisterCallbackHandler(BiosKeyboardInt9Handler);
        KeyboardInt16Handler = new(machine, BiosKeyboardInt9Handler.BiosKeyboardBuffer, loggerService);
        machine.RegisterCallbackHandler(KeyboardInt16Handler);
    }
}