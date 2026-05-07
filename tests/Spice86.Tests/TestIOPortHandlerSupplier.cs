namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Test double used to verify reflection-based instantiation of
/// <see cref="IIOPortHandlerSupplier"/> implementations through
/// <see cref="CommandLineParser.ParseIOPortHandlerSupplierClassName(string?)"/>.
/// </summary>
public class TestIOPortHandlerSupplier : IIOPortHandlerSupplier {
    public void RegisterIOPortHandlers(
        IOPortDispatcher ioPortDispatcher,
        ILoggerService loggerService,
        Configuration configuration,
        Machine machine) {
        // No-op: used by reflection-based instantiation tests.
    }
}
