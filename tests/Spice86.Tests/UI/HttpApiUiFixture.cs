namespace Spice86.Tests.UI;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;

public sealed class HttpApiUiFixture : IDisposable {
    public HttpApiUiFixture() {
        LoggerService loggerService = new() {
            AreLogsSilenced = true
        };

        State state = new(CpuModel.INTEL_80286) {
            CS = 0x1234,
            IP = 0x5678,
            IsRunning = true
        };

        for (int i = 0; i < 64; i++) {
            state.IncCycles();
        }

        AddressReadWriteBreakpoints breakpoints = new();
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new(false);
        Memory memory = new(breakpoints, ram, a20Gate);
        memory[0x40] = 0x12;

        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        pauseHandler.IsPaused.Returns(false);

        State = state;
        Memory = memory;
        Server = new Spice86HttpApiServer(state, memory, pauseHandler, loggerService);
    }

    public State State { get; }

    public Memory Memory { get; }

    public Spice86HttpApiServer Server { get; }

    public void Dispose() {
        Server.Dispose();
    }
}
