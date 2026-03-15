namespace Spice86.Core.Emulator.Http;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Runtime state exposed by HTTP API controllers.
/// </summary>
public sealed class HttpApiState {
    /// <summary>Initializes a new instance of <see cref="HttpApiState"/>.</summary>
    /// <param name="state">CPU register and flag state of the emulator.</param>
    /// <param name="memory">Emulator memory bus.</param>
    /// <param name="pauseHandler">Handler used to query and change the emulator pause state.</param>
    public HttpApiState(State state, IMemory memory, IPauseHandler pauseHandler) {
        State = state;
        Memory = memory;
        PauseHandler = pauseHandler;
    }

    /// <summary>CPU register and flag state of the emulator.</summary>
    public State State { get; }

    /// <summary>
    /// Emulator memory bus. Memory operations are performed via
    /// <see cref="IMemory.SneakilyRead"/> and <see cref="IMemory.SneakilyWrite"/>
    /// to avoid triggering breakpoints from Kestrel request threads.
    /// Note: concurrent access with the emulator main thread is not prevented by this class.
    /// </summary>
    public IMemory Memory { get; }

    /// <summary>Handler used to query and change the emulator pause state.</summary>
    public IPauseHandler PauseHandler { get; }
}
