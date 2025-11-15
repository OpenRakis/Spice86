namespace Spice86.Core.Emulator.IOPorts;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
///     Delegate used by port subscribers to model byte and word reads.
/// </summary>
/// <param name="port">16-bit I/O port being read.</param>
/// <returns>Value driven onto the bus.</returns>
public delegate uint IoReadDelegate(ushort port);

/// <summary>
///     Tracks the lifecycle of a read delegate within the deterministic I/O fabric.
/// </summary>
internal sealed class IoReadHandler(IOPortHandlerRegistry portHandlerRegistry, IoReadDelegate handler, ILoggerService logger) {
    private ushort? _installedPort;
    private ushort? _installedPortRange;

    /// <summary>
    ///     Registers the delegate with the underlying <see cref="IOPortHandlerRegistry" /> for the specified port range.
    /// </summary>
    /// <param name="port">First I/O port covered by the delegate.</param>
    /// <param name="portRange">Number of consecutive ports handled by this delegate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the handler has already been installed.</exception>
    public void Install(ushort port, ushort portRange = 1) {
        if (_installedPort != null) {
            logger.Error(
                "Attempted to install read handler on port 0x{Port:X4} while already installed on port 0x{InstalledPort:X4} (range {InstalledRange})",
                port, _installedPort.Value, _installedPortRange);
            throw new InvalidOperationException($"{nameof(IoReadHandler)} already installed.");
        }

        _installedPort = port;
        _installedPortRange = portRange;
        portHandlerRegistry.RegisterReadHandler(handler, port, portRange);
        logger.Debug("Installed read handler on port 0x{Port:X4} with range {PortRange}", port, portRange);
    }

    /// <summary>
    ///     Removes a previously registered delegate from the <see cref="IOPortHandlerRegistry" />.
    /// </summary>
    public void Uninstall() {
        if (_installedPort == null) {
            if (logger.IsEnabled(LogEventLevel.Debug)) {
                logger.Debug("Uninstall requested for read handler, but nothing was installed.");
            }

            return;
        }

        ushort port = _installedPort.Value;
        ushort range = _installedPortRange!.Value;
        portHandlerRegistry.FreeHandler(port, range);
        _installedPort = null;
        _installedPortRange = null;
        logger.Debug("Uninstalled read handler previously mapped to port 0x{Port:X4} with range {PortRange}", port,
            range);
    }
}