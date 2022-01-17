namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Linq;

public class GdbCommandHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbCommandHandler>();
    private bool connected = true;
    private GdbCommandBreakpointHandler gdbCommandBreakpointHandler;
    private GdbCommandMemoryHandler gdbCommandMemoryHandler;
    private GdbCommandRegisterHandler gdbCommandRegisterHandler;
    private GdbCustomCommandsHandler gdbCustomCommandsHandler;
    private GdbIo gdbIo;
    private Machine machine;

    public GdbCommandHandler(GdbIo gdbIo, Machine machine, string defaultDumpDirectory) {
        this.gdbIo = gdbIo;
        this.machine = machine;
        this.gdbCommandRegisterHandler = new GdbCommandRegisterHandler(gdbIo, machine);
        this.gdbCommandMemoryHandler = new GdbCommandMemoryHandler(gdbIo, machine);
        this.gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(gdbIo, machine);
        this.gdbCustomCommandsHandler = new GdbCustomCommandsHandler(gdbIo, machine, gdbCommandBreakpointHandler.OnBreakPointReached, defaultDumpDirectory);
    }

    public bool IsConnected() {
        return connected;
    }

    public void PauseEmulator() {
        gdbCommandBreakpointHandler.SetResumeEmulatorOnCommandEnd(false);
        machine.GetMachineBreakpoints().GetPauseHandler().RequestPause();
    }

    public void RunCommand(string command) {
        _logger.Information("Received command {@Command}", command);
        char first = command[0];
        string commandContent = command.Substring(1);
        PauseHandler pauseHandler = machine.GetMachineBreakpoints().GetPauseHandler();
        pauseHandler.RequestPauseAndWait();
        try {
            string response = first switch {
                (char)0x03 => gdbCommandBreakpointHandler.Step(),
                'k' => Kill(),
                'D' => Detach(),
                'c' => gdbCommandBreakpointHandler.ContinueCommand(),
                'H' => SetThreadContext(),
                'q' => QueryVariable(commandContent),
                '?' => ReasonHalted(),
                'g' => gdbCommandRegisterHandler.ReadAllRegisters(),
                'G' => gdbCommandRegisterHandler.WriteAllRegisters(commandContent),
                'p' => gdbCommandRegisterHandler.ReadRegister(commandContent),
                'P' => gdbCommandRegisterHandler.WriteRegister(commandContent),
                'm' => gdbCommandMemoryHandler.ReadMemory(commandContent),
                'M' => gdbCommandMemoryHandler.WriteMemory(commandContent),
                'T' => HandleThreadALive(),
                'v' => ProcessVPacket(commandContent),
                's' => gdbCommandBreakpointHandler.Step(),
                'z' => gdbCommandBreakpointHandler.RemoveBreakpoint(commandContent),
                'Z' => gdbCommandBreakpointHandler.AddBreakpoint(commandContent),
                _ => gdbIo.GenerateUnsupportedResponse()
            };
            if (response != null) {
                gdbIo.SendResponse(response);
            }
        } finally {
            if (gdbCommandBreakpointHandler.IsResumeEmulatorOnCommandEnd()) {
                pauseHandler.RequestResume();
            }
        }
    }

    private string Detach() {
        connected = false;
        gdbCommandBreakpointHandler.SetResumeEmulatorOnCommandEnd(true);
        return gdbIo.GenerateResponse("");
    }

    private string HandleThreadALive() {
        return gdbIo.GenerateResponse("OK");
    }

    private string Kill() {
        machine.GetCpu().SetRunning(false);
        return Detach();
    }

    private Tuple<string, object> ParseSupportedQuery(string item) {
        Tuple<string, object> res;
        if (item.EndsWith("+")) {
            res = Tuple.Create(item.Substring(0, item.Length - 1), (object)true);
        } else if (item.EndsWith("-")) {
            res = Tuple.Create(item.Substring(0, item.Length - 1), (object)false);
        } else {
            String[] split = item.Split("=");
            res = Tuple.Create(split[0], new object());
            if (split.Length == 2) {
                res = Tuple.Create(split[0], (object)split[1]);
            }
        }

        return res;
    }

    private string ProcessVPacket(string commandContent) {
        return (commandContent) switch {
            "MustReplyEmpty" => gdbIo.GenerateResponse(""),
            "Cont?" => gdbIo.GenerateResponse(""),
            _ => gdbIo.GenerateUnsupportedResponse()
        };
    }

    private string QueryVariable(string command) {
        if (command.StartsWith("Supported:")) {
            string[] supportedRequestItems = command.Replace("Supported:", "").Split(";");
            Dictionary<string, object> supportedRequest = supportedRequestItems
                .ToDictionary(x => ParseSupportedQuery(x))
                .ToDictionary(data => (string)data.Key.Item1, data => data.Key.Item2);
            if (supportedRequest.TryGetValue("xmlRegisters", out var value) == false || value.Equals("i386") == false) {
                return gdbIo.GenerateUnsupportedResponse();
            }

            return gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("L")) {
            string nextthread = command.Substring(4);
            return gdbIo.GenerateResponse($"qM011{nextthread}00000001");
        }

        if (command.StartsWith("P")) {
            return gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("ThreadExtraInfo")) {
            return gdbIo.GenerateMessageToDisplayResponse("spice86");
        }

        if (command.StartsWith("Rcmd")) {
            return gdbCustomCommandsHandler.HandleCustomCommands(command);
        }

        if (command.StartsWith("Search")) {
            return gdbCommandMemoryHandler.SearchMemory(command);
        }

        return "";
    }

    private string ReasonHalted() {
        return gdbIo.GenerateResponse("S05");
    }

    private string SetThreadContext() {
        return gdbIo.GenerateResponse("OK");
    }
}