namespace Spice86.Tests.AsmFixtures;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;

/// <summary>A test port that returns the last value written, truncated to the read width.</summary>
internal sealed class LatchIoPortHandler : DefaultIOPortHandler {
    private uint _value;

    /// <summary>Creates the handler and registers it on the given port.</summary>
    /// <param name="state">CPU state, used by error reporting.</param>
    /// <param name="loggerService">Logger used by the base port handler.</param>
    /// <param name="ioPortDispatcher">Dispatcher the handler registers itself on.</param>
    /// <param name="port">Port the latch listens on.</param>
    public LatchIoPortHandler(State state, ILogger loggerService, IOPortDispatcher ioPortDispatcher, ushort port)
        : base(state, true, loggerService) {
        ioPortDispatcher.AddIOPortHandler(port, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return (byte)_value;
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        return (ushort)_value;
    }

    /// <inheritdoc />
    public override uint ReadDWord(ushort port) {
        return _value;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _value = value;
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        _value = value;
    }

    /// <inheritdoc />
    public override void WriteDWord(ushort port, uint value) {
        _value = value;
    }
}
