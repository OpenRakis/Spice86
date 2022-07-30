namespace Spice86.Core.Emulator.IOPorts;

using System.Collections.Generic;

using Serilog;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

/// <summary>
/// Handles calling the correct dispatcher depending on port number for I/O reads and writes.
/// </summary>
public class IOPortDispatcher : DefaultIOPortHandler {
    private readonly Dictionary<int, IIOPortHandler> _ioPortHandlers = new();

    private readonly ILogger _logger = new Serilogger().Logger.ForContext<IOPortDispatcher>();

    public IOPortDispatcher(Machine machine, Configuration configuration) : base(machine, configuration) {
        _failOnUnhandledPort = configuration.FailOnUnhandledPort;
    }

    public void AddIOPortHandler(int port, IIOPortHandler ioPortHandler) {
        _ioPortHandlers.Add(port, ioPortHandler);
    }

    public override byte ReadByte(int port) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            _logger.Debug("{MethodName} {PortHandlerTypeName} {PortNumber}", nameof(ReadByte), entry.GetType(), port);
            return entry.ReadByte(port);
        }

        return base.ReadByte(port);
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    public override ushort ReadWord(int port) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            _logger.Debug("{MethodName} {PortHandlerTypeName} {PortNumber}", nameof(ReadWord), entry.GetType(), port);
            return entry.ReadWord(port);
        }

        return base.ReadWord(port);
    }

    public override void WriteByte(int port, byte value) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            _logger.Debug("{MethodName} {PortHandlerTypeName} {PortNumber} {WrittenValue}", nameof(WriteByte), entry.GetType(), port, value);
            entry.WriteByte(port, value);
        } else {
            base.WriteByte(port, value);
        }
    }

    public override void WriteWord(int port, ushort value) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            _logger.Debug("{MethodName} {PortHandlerTypeName} {PortNumber} {WrittenValue}", nameof(WriteWord), entry.GetType(), port, value);
            entry.WriteWord(port, value);
        } else {
            base.WriteWord(port, value);
        }
    }
}
