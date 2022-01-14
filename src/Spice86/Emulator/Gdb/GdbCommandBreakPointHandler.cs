namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Machine.Breakpoint;
using Spice86.Utils;

using System;
using System.IO;

public class GdbCommandBreakpointHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbCommandBreakpointHandler>();
    private GdbIo gdbIo;
    private Machine machine;
    private volatile bool resumeEmulatorOnCommandEnd;

    public GdbCommandBreakpointHandler(GdbIo gdbIo, Machine machine) {
        this.gdbIo = gdbIo;
        this.machine = machine;
    }

    public string AddBreakpoint(string commandContent) {
        BreakPoint? breakPoint = ParseBreakPoint(commandContent);
        machine.GetMachineBreakpoints().ToggleBreakPoint(breakPoint, true);
        _logger.Debug("Breakpoint added!\\n{@BreakPoint}", breakPoint);
        return gdbIo.GenerateResponse("OK");
    }

    public string ContinueCommand() {
        resumeEmulatorOnCommandEnd = true;
        machine.GetMachineBreakpoints().GetPauseHandler().RequestResume();

        // Do not send anything to GDB, CPU thread will send something when breakpoint is reached
        return gdbIo.GenerateResponse("OK");
    }

    public bool IsResumeEmulatorOnCommandEnd() {
        return resumeEmulatorOnCommandEnd;
    }

    public void OnBreakPointReached(BreakPoint breakPoint) {
        _logger.Debug("Breakpoint reached!\\n{@BreakPoint}", breakPoint);
        machine.GetMachineBreakpoints().GetPauseHandler().RequestPause();
        resumeEmulatorOnCommandEnd = false;
        try {
            gdbIo.SendResponse(gdbIo.GenerateResponse("S05"));
        } catch (IOException e) {
            _logger.Error(e, "IOException while sending breakpoint info");
        }
    }

    public BreakPoint? ParseBreakPoint(String command) {
        try {
            string[] commandSplit = command.Split(",");
            int type = int.Parse(commandSplit[0]);
            long address = ConvertUtils.ParseHex32(commandSplit[1]);
            // 3rd parameter kind is unused in our case
            BreakPointType? breakPointType = type switch {
                0 => BreakPointType.EXECUTION,
                1 => BreakPointType.EXECUTION,
                2 => BreakPointType.WRITE,
                3 => BreakPointType.READ,
                4 => BreakPointType.ACCESS,
                _ => null
            };
            if (breakPointType == null) {
                _logger.Error("Cannot parse breakpoint type {@Type} for command {@Command}", type, command);
            }
            return new BreakPoint(breakPointType, address, this.OnBreakPointReached, false);
        } catch (FormatException nfe) {
            _logger.Error(nfe, "Cannot parse breakpoint {@Command}", command);
            return null;
        }
    }

    public string RemoveBreakpoint(string commandContent) {
        BreakPoint breakPoint = ParseBreakPoint(commandContent);
        machine.GetMachineBreakpoints().ToggleBreakPoint(breakPoint, false);
        _logger.Debug("Breakpoint removed!\\n{@BreakPoint}", breakPoint);
        return gdbIo.GenerateResponse("OK");
    }

    public void SetResumeEmulatorOnCommandEnd(bool resumeEmulatorOnCommandEnd) {
        this.resumeEmulatorOnCommandEnd = resumeEmulatorOnCommandEnd;
    }

    public string Step() {
        resumeEmulatorOnCommandEnd = true;

        // will pause the CPU at the next instruction unconditionally
        BreakPoint stepBreakPoint = new UnconditionalBreakPoint(BreakPointType.EXECUTION, this.OnBreakPointReached, true);
        machine.GetMachineBreakpoints().ToggleBreakPoint(stepBreakPoint, true);
        _logger.Debug("Breakpoint added for step!\\n{@StepBreakPoint}", stepBreakPoint);

        // Do not send anything to GDB, CPU thread will send something when breakpoint is reached
        return null;
    }
}