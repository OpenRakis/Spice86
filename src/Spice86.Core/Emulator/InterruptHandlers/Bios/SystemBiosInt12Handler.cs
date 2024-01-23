namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     INT 12h handler. Reports how many kb of base memory is installed.
/// </summary>
/// <summary>
///     INT 12h handler. Reports how many kb of base memory is installed.
/// </summary>
public class SystemBiosInt12Handler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    ///     Initializes a new instance.
    /// </summary>
    /// <param name="memory"></param>
    /// <param name="cpu"></param>
    /// <param name="biosDataArea"></param>
    /// <param name="loggerService"></param>
    public SystemBiosInt12Handler(IMemory memory, Cpu cpu, BiosDataArea biosDataArea, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        _biosDataArea = biosDataArea;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x12;

    /// <inheritdoc />
    public override void Run() {
        State.AX = _biosDataArea.ConventionalMemorySizeKb;
    }
}