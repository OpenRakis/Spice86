namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// <para>Reimplementation of int28</para>
/// <para>This is a way of letting DOS know that the application is idle and that it can perform other tasks.</para>
/// </summary>
public class DosInt28Handler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt28Handler(IMemory memory, Cpu cpu, ILoggerService loggerService) : base(memory, cpu, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x28;

    /// <inheritdoc />
    public override void Run() {
        LoggerService.Verbose("DOS IDLE");
    }
}