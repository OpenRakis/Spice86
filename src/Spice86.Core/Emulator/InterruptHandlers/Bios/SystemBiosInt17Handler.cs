using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Bios;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS INT 17h printer services.
/// </summary>
public sealed class SystemBiosInt17Handler : InterruptHandler {
    private const ushort DefaultLpt1BasePort = 0x0378;
    private const ushort DefaultLpt2BasePort = 0x0278;
    private const ushort DefaultLpt3BasePort = 0x03BC;
    private const byte DefaultTimeoutSeconds = 1;
    private const byte PrinterWriteTimeoutStatus = 0x01;
    private const byte PrinterReadyStatus = 0x00;

    private readonly BiosDataArea _biosDataArea;

    public SystemBiosInt17Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        Stack stack,
        State state,
        BiosDataArea biosDataArea,
        ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        InitializeBiosDataArea();
    }

    public override byte VectorNumber => 0x17;

    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: AH=0x{Function:X2} DX=0x{PrinterIndex:X4} AL=0x{Character:X2}",
                operation, State.DX, State.AL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("BIOS INT17H: Unsupported function AH=0x{Function:X2}", operation);
            }
            SetCarryFlag(true, true);
            return;
        }

        Run(operation);
    }


    private void InitializeBiosDataArea() {
        if (_biosDataArea.PortLpt[0] == 0) {
            _biosDataArea.PortLpt[0] = DefaultLpt1BasePort;
        }
        if (_biosDataArea.PortLpt[1] == 0) {
            _biosDataArea.PortLpt[1] = DefaultLpt2BasePort;
        }
        if (_biosDataArea.PortLpt[2] == 0) {
            _biosDataArea.PortLpt[2] = DefaultLpt3BasePort;
        }

        for (int i = 0; i < 4; i++) {
            if (_biosDataArea.LptTimeout[i] == 0) {
                _biosDataArea.LptTimeout[i] = DefaultTimeoutSeconds;
            }
        }
    }

    /// <summary>Writes <paramref name="character"/> to printer <paramref name="printerIndex"/> and
    /// returns the status in AH with CF clear.</summary>
    public (byte ah, bool carryFlag) WriteCharacter(byte character, ushort printerIndex) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Write character 0x{Character:X2} to printer index {PrinterIndex}",
                character, printerIndex);
        }

        return (PrinterWriteTimeoutStatus, false);
    }

    [InterruptService(0x00, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None,
        Implementation = nameof(WriteCharacter), Inputs = [CpuValues.Al, CpuValues.Dx])]
    private void WriteCharacter(bool calledFromVm) {
        (byte ah, bool carryFlag) = WriteCharacter(State.AL, State.DX);
        State.AH = ah;
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Initializes printer port <paramref name="printerIndex"/> and returns the status
    /// in AH with CF clear.</summary>
    public (byte ah, bool carryFlag) InitializePrinterPort(ushort printerIndex) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Initialize printer index {PrinterIndex}", printerIndex);
        }

        return (PrinterReadyStatus, false);
    }

    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None,
        Implementation = nameof(InitializePrinterPort), Inputs = [CpuValues.Dx])]
    private void InitializePrinterPort(bool calledFromVm) {
        (byte ah, bool carryFlag) = InitializePrinterPort(State.DX);
        State.AH = ah;
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns the status of printer <paramref name="printerIndex"/> in AH with CF
    /// clear.</summary>
    public (byte ah, bool carryFlag) GetPrinterStatus(ushort printerIndex) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Get status for printer index {PrinterIndex}", printerIndex);
        }

        return (PrinterReadyStatus, false);
    }

    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None,
        Implementation = nameof(GetPrinterStatus), Inputs = [CpuValues.Dx])]
    private void GetPrinterStatus(bool calledFromVm) {
        (byte ah, bool carryFlag) = GetPrinterStatus(State.DX);
        State.AH = ah;
        SetCarryFlag(carryFlag, calledFromVm);
    }
}
