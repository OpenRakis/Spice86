namespace Spice86.Tests.AsmFixtures;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;

/// <summary>
/// Captures the POST and ASCII debug port writes of test386 and fails on a repeated POST value, which means
/// the test is looping.
/// </summary>
internal sealed class Test386PostPortHandler : DefaultIOPortHandler {
    private const int PostPort = 0x999;
    private const int AsciiOutPort = 0x998;

    /// <summary>The POST values the program sent, in order of arrival.</summary>
    public List<ushort> PostValues { get; } = new();

    /// <summary>The ASCII text the program wrote to the debug port.</summary>
    public string AsciiError { get; private set; } = "";

    /// <summary>Creates the handler and registers it on the POST and ASCII debug ports.</summary>
    /// <param name="state">CPU state, used by error reporting.</param>
    /// <param name="loggerService">Logger used by the base port handler.</param>
    /// <param name="ioPortDispatcher">Dispatcher the handler registers itself on.</param>
    public Test386PostPortHandler(State state, ILogger loggerService, IOPortDispatcher ioPortDispatcher)
        : base(state, true, loggerService) {
        ioPortDispatcher.AddIOPortHandler(PostPort, this);
        ioPortDispatcher.AddIOPortHandler(AsciiOutPort, this);
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (port == AsciiOutPort) {
            AsciiError += System.Text.Encoding.ASCII.GetString([value]);
        } else if (port == PostPort) {
            if (PostValues.Contains(value)) {
                throw new UnhandledOperationException(_state, $"POST value {value} already sent. Is test looping?");
            }

            PostValues.Add(value);
        }
    }
}
