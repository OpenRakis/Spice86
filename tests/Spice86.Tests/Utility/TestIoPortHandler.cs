namespace Spice86.Tests.Utility;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

/// <summary>
/// Shared I/O port handler that captures test result and detail bytes
/// written by assembly test programs to ports 0x999 and 0x998.
/// </summary>
public sealed class TestIoPortHandler : DefaultIOPortHandler {
    public const int ResultPort = 0x999;
    public const int DetailsPort = 0x998;

    public List<byte> Results { get; } = new();
    public List<byte> Details { get; } = new();

    public TestIoPortHandler(State state, ILoggerService loggerService, IOPortDispatcher ioPortDispatcher)
        : base(state, true, loggerService) {
        ioPortDispatcher.AddIOPortHandler(ResultPort, this);
        ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
    }

    public override void WriteByte(ushort port, byte value) {
        if (port == ResultPort) {
            Results.Add(value);
        } else if (port == DetailsPort) {
            Details.Add(value);
        }
    }
}
