namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 2Ah - Network and Critical Section.
/// Minimal stub, only mocks function AH=0 (Network Installation Query), by
/// returning 0 (no network hardware installed).
/// </summary>
public class DosInt2aHandler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public DosInt2aHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2A;

    private void FillDispatchTable() {
        AddAction(0x0, NetworkInstallationQuery);
    }

    /// <summary>
    /// Stub - If AH == 0, return 0 in AH (network is not installed)
    ///
    /// Even if the stub is conceptually a nop, note that the input and
    /// output values have different meanings. The input 0 selects the function
    /// (network installation query), the output 0 notifies that no network HW
    /// is detected.
    /// </summary>
    public void NetworkInstallationQuery() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 2Ah AH=00: Network Installation Query");
        }
        bool isNetworkInstalled = false; // Stub

        State.AH = (byte)(isNetworkInstalled ? 0x01 : 0x00);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (!HasRunnable(operation)) {
            // For any other AH subfunction, respond with a generic "not implemented" error.
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("INT 2Ah AH={AH:X2}: operation not implemented", State.AH);
            }
            // DOS convention: set Carry Flag and set AX to an error code. 0x01 = Invalid function.
            State.AX = 0x0001;
            SetCarryFlag(true, setOnStack: true);
            return;
        }

        Run(operation);
    }
}
