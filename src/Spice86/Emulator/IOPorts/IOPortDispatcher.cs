namespace Spice86.Emulator.IOPorts;

using Spice86.Emulator.VM;

using System.Collections.Generic;

/// <summary>
/// Handles calling the correct dispatcher depending on port number for I/O reads and writes.
/// </summary>
public class IOPortDispatcher : DefaultIOPortHandler {
    private readonly Dictionary<int, IIOPortHandler> _ioPortHandlers = new();

    public IOPortDispatcher(Machine machine, Configuration configuration) : base(machine, configuration) {
        this._failOnUnhandledPort = configuration.FailOnUnhandledPort;
    }

    public void AddIOPortHandler(int port, IIOPortHandler ioPortHandler) {
        _ioPortHandlers.Add(port, ioPortHandler);
    }

    public override byte ReadByte(int port) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            return entry.ReadByte(port);
        }

        return base.ReadByte(port);
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    public override ushort ReadWord(int port) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            return entry.ReadWord(port);
        }

        return base.ReadWord(port);
    }

    public override void WriteByte(int port, byte value) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            entry.WriteByte(port, value);
        } else {
            base.WriteByte(port, value);
        }
    }

    public override void WriteWord(int port, ushort value) {
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            entry.WriteWord(port, value);
        } else {
            base.WriteWord(port, value);
        }
    }
}
