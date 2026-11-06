namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Platform;
using Spice86.Shared.Interfaces;

/// <summary>
/// Returns the guest-visible BIOS equipment word for interrupt 11h.
/// </summary>
public class BiosEquipmentDeterminationInt11Handler : InterruptHandler {
    private readonly MachineProfile _machineProfile;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation</param>
    public BiosEquipmentDeterminationInt11Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILogger loggerService)
        : this(memory, functionHandlerProvider, stack, state, loggerService, MachineProfile.For(MachineType.Pc)) {
    }

    /// <summary>
    /// Initializes platform equipment reporting while preserving the legacy PC result.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">The call-flow handler provider.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger.</param>
    /// <param name="machineProfile">The selected platform.</param>
    public BiosEquipmentDeterminationInt11Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ILogger loggerService, MachineProfile machineProfile)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _machineProfile = machineProfile;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x11;

    /// <inheritdoc />
    public override void Run() => GetEquipmentList();

    [InterruptService(Writes = CpuValues.Ax)]
    private void GetEquipmentList() {
        State.AX = _machineProfile.ReadEquipmentWord(Memory);
    }
}