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

    [InterruptService(0x00, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None)]
    private void WriteCharacter(bool calledFromVm) {
        ushort printerIndex = State.DX;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Write character 0x{Character:X2} to printer index {PrinterIndex}",
                State.AL, printerIndex);
        }

        State.AH = PrinterWriteTimeoutStatus;
        SetCarryFlag(false, calledFromVm);
    }

    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None)]
    private void InitializePrinterPort(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Initialize printer index {PrinterIndex}", State.DX);
        }

        State.AH = PrinterReadyStatus;
        SetCarryFlag(false, calledFromVm);
    }

    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None)]
    private void GetPrinterStatus(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT17H: Get status for printer index {PrinterIndex}", State.DX);
        }

        State.AH = PrinterReadyStatus;
        SetCarryFlag(false, calledFromVm);
    }
}
