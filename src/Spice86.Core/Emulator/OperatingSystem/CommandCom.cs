namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Simulates COMMAND.COM PSP so processes have a root parent.
/// </summary>
public class CommandCom {
    public const ushort CommandComSegment = 0x60;
    private const ushort JftOffset = 0x18;
    private readonly DosProgramSegmentPrefix _psp;

    public ushort PspSegment => CommandComSegment;

    public CommandCom(IMemory memory, ILoggerService loggerService) {
        _psp = new DosProgramSegmentPrefix(memory, MemoryUtils.ToPhysicalAddress(CommandComSegment, 0));
        InitializePsp();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information(
                "COMMAND.COM PSP initialized at segment {Segment:X4}",
                CommandComSegment);
        }
    }

    public ushort NextSegment {
        get => _psp.NextSegment;
        set => _psp.NextSegment = value;
    }

    private void InitializePsp() {
        _psp.Exit[0] = 0xCD;
        _psp.Exit[1] = 0x20;
        _psp.NextSegment = (ushort)(CommandComSegment + 0x10);
        _psp.FarCall = 0x9A;
        _psp.CpmServiceRequestAddress = 0;
        _psp.TerminateAddress = MemoryUtils.ToPhysicalAddress(CommandComSegment, 0);
        _psp.BreakAddress = 0;
        _psp.CriticalErrorAddress = 0;
        _psp.ParentProgramSegmentPrefix = CommandComSegment;

        for (int i = 0; i < 20; i++) {
            _psp.Files[i] = 0xFF;
        }
        _psp.Files[0] = 0;
        _psp.Files[1] = 1;
        _psp.Files[2] = 2;
        _psp.Files[3] = 3;
        _psp.Files[4] = 4;

        _psp.EnvironmentTableSegment = 0;
        _psp.StackPointer = 0;
        _psp.MaximumOpenFiles = 20;
        _psp.FileTableAddress = MemoryUtils.ToPhysicalAddress(CommandComSegment, JftOffset);
        _psp.PreviousPspAddress = 0;
        _psp.InterimFlag = 0;
        _psp.TrueNameFlag = 0;
        _psp.NNFlags = 0;
        _psp.DosVersionMajor = 5;
        _psp.DosVersionMinor = 0;
        _psp.Service[0] = 0xCD;
        _psp.Service[1] = 0x21;
        _psp.Service[2] = 0xCB;
    }
}
