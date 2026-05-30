namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

/// <summary>
/// Captures the POST and ASCII debug port writes emitted by the test386 fixture so the generated-code
/// run can assert the same "test finished normally" outcome as the emulated-mode <c>MachineTest</c>.
/// </summary>
internal sealed class Test386PostPortHandler : DefaultIOPortHandler {
    private const int PostPort = 0x999;
    private const int AsciiOutPort = 0x998;

    public List<ushort> PostValues { get; } = new();
    public string AsciiError { get; private set; } = "";

    public Test386PostPortHandler(State state, ILoggerService loggerService, IOPortDispatcher ioPortDispatcher)
        : base(state, true, loggerService) {
        ioPortDispatcher.AddIOPortHandler(PostPort, this);
        ioPortDispatcher.AddIOPortHandler(AsciiOutPort, this);
    }

    public override void WriteByte(ushort port, byte value) {
        if (port == AsciiOutPort) {
            AsciiError += System.Text.Encoding.ASCII.GetString([value]);
        } else if (port == PostPort) {
            PostValues.Add(value);
        }
    }
}
