namespace Spice86.Core.Emulator.IOPorts;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides an extension point for registering custom I/O port handlers from an external assembly.
/// </summary>
/// <remarks>
/// Implement this interface to attach handlers for unusual hardware ports that Spice86 does not emulate
/// natively (for example, machine-specific configuration chips, custom keyboard controllers, or hardware
/// detection ports). The supplier class name is selected at startup via the
/// <c>--IOPortHandlerSupplierClassName</c> command-line option and instantiated by reflection.
/// </remarks>
public interface IIOPortHandlerSupplier {
    /// <summary>
    /// Registers custom I/O port handlers on the given dispatcher.
    /// </summary>
    /// <param name="ioPortDispatcher">
    /// The dispatcher to register handlers on. Use
    /// <see cref="IOPortDispatcher.AddIOPortHandler(int, IIOPortHandler)"/> to register a handler for a port
    /// not already used by Spice86, or
    /// <see cref="IOPortDispatcher.OverrideIOPortHandler(int, IIOPortHandler)"/> to replace a built-in
    /// handler.
    /// </param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="configuration">The active emulator configuration.</param>
    /// <param name="machine">The fully constructed emulator machine, providing access to CPU state, memory and devices.</param>
    void RegisterIOPortHandlers(
        IOPortDispatcher ioPortDispatcher,
        ILoggerService loggerService,
        Configuration configuration,
        Machine machine);
}
