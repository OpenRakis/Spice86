namespace Spice86.Tests.Http;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Http;
using Spice86.Logging;

public sealed class HttpApiServerFixture : IDisposable {
    public HttpApiServerFixture() {
        LoggerService loggerService = new() {
            AreLogsSilenced = true
        };

        State state = new(CpuModel.INTEL_80286) {
            CS = 0x1234,
            IP = 0x5678,
            IsRunning = true
        };

        for (int i = 0; i < 128; i++) {
            state.IncCycles();
        }

        AddressReadWriteBreakpoints breakpoints = new();
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new(false);
        Memory memory = new(breakpoints, ram, a20Gate);
        memory[0x40] = 0x12;
        memory[0x41] = 0x34;
        memory[0x42] = 0x56;
        memory[0x43] = 0x78;

        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        pauseHandler.IsPaused.Returns(false);

        State = state;
        Memory = memory;
        // Use port 0 to let the OS assign an available ephemeral port, avoiding conflicts with other tests.
        Server = new Spice86HttpApiServer(state, memory, pauseHandler, loggerService, port: 0);
        HttpClient = new HttpClient {
            BaseAddress = new Uri($"http://{HttpApiEndpoint.Host}:{Server.Port}")
        };
    }

    public State State { get; }

    public Memory Memory { get; }

    public HttpClient HttpClient { get; }

    public Spice86HttpApiServer Server { get; }

    public void Dispose() {
        HttpClient.Dispose();
        Server.Dispose();
    }
}
