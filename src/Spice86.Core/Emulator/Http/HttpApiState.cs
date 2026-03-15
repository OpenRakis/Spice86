namespace Spice86.Core.Emulator.Http;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Runtime state exposed by HTTP API controllers.
/// </summary>
public sealed class HttpApiState {
    /// <summary>Initializes a new instance of <see cref="HttpApiState"/>.</summary>
    /// <param name="state">CPU register and flag state of the emulator.</param>
    /// <param name="memory">Emulator memory bus.</param>
    public HttpApiState(State state, IMemory memory) {
        State = state;
        Memory = memory;
    }

    /// <summary>CPU register and flag state of the emulator.</summary>
    public State State { get; }

    /// <summary>
    /// Emulator memory bus. Memory access goes through the <see cref="IMemory"/> indexer
    /// which applies address transformation and breakpoint monitoring.
    /// Note: concurrent access with the emulator main thread is not prevented by this class.
    /// </summary>
    public IMemory Memory { get; }
}
