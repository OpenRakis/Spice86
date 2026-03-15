namespace Spice86.Core.Emulator.Http;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Runtime state exposed by HTTP API controllers.
/// </summary>
public sealed class HttpApiState {
    /// <summary>
    /// Lock used to serialize all memory read/write operations from Kestrel request threads
    /// to prevent concurrent access with the emulator main thread.
    /// </summary>
    public readonly object MemoryLock = new();

    public HttpApiState(State state, IMemory memory, IPauseHandler pauseHandler) {
        State = state;
        Memory = memory;
        PauseHandler = pauseHandler;
    }

    public State State { get; }

    public IMemory Memory { get; }

    public IPauseHandler PauseHandler { get; }
}
