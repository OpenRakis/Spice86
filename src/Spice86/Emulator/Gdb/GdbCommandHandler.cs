namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.VM;

using System;
using System.Linq;

public class GdbCommandHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<GdbCommandHandler>();
    private bool _isConnected = true;
    private readonly GdbCommandBreakpointHandler _gdbCommandBreakpointHandler;
    private readonly GdbCommandMemoryHandler _gdbCommandMemoryHandler;
    private readonly GdbCommandRegisterHandler _gdbCommandRegisterHandler;
    private readonly GdbCustomCommandsHandler _gdbCustomCommandsHandler;
    private readonly GdbIo _gdbIo;
    private readonly Machine _machine;

    public GdbCommandHandler(GdbIo gdbIo, Machine machine, Configuration configuration) {
        this._gdbIo = gdbIo;
        this._machine = machine;
        this._gdbCommandRegisterHandler = new GdbCommandRegisterHandler(gdbIo, machine);
        this._gdbCommandMemoryHandler = new GdbCommandMemoryHandler(gdbIo, machine);
        this._gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(gdbIo, machine);
        this._gdbCustomCommandsHandler = new GdbCustomCommandsHandler(gdbIo, machine, _gdbCommandBreakpointHandler.OnBreakPointReached, configuration.RecordedDataDirectory);
    }

    public bool IsConnected => _isConnected;

    public void PauseEmulator() {
        _gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd = false;
        _machine.MachineBreakpoints.PauseHandler.RequestPause();
    }

    public void Step() => _gdbCommandBreakpointHandler.Step();

    public void RunCommand(string command) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Received command {@Command}", command);
        }
        char first = command[0];
        string commandContent = command[1..];
        PauseHandler pauseHandler = _machine.MachineBreakpoints.PauseHandler;
        pauseHandler.RequestPauseAndWait();
        try {
            string? response = first switch {
                (char)0x03 => _gdbCommandBreakpointHandler.Step(),
                'k' => Kill(),
                'D' => Detach(),
                'c' => _gdbCommandBreakpointHandler.ContinueCommand(),
                'H' => SetThreadContext(),
                'q' => QueryVariable(commandContent),
                '?' => ReasonHalted(),
                'g' => _gdbCommandRegisterHandler.ReadAllRegisters(),
                'G' => _gdbCommandRegisterHandler.WriteAllRegisters(commandContent),
                'p' => _gdbCommandRegisterHandler.ReadRegister(commandContent),
                'P' => _gdbCommandRegisterHandler.WriteRegister(commandContent),
                'm' => _gdbCommandMemoryHandler.ReadMemory(commandContent),
                'M' => _gdbCommandMemoryHandler.WriteMemory(commandContent),
                'T' => HandleThreadALive(),
                'v' => ProcessVPacket(commandContent),
                's' => _gdbCommandBreakpointHandler.Step(),
                'z' => _gdbCommandBreakpointHandler.RemoveBreakpoint(commandContent),
                'Z' => _gdbCommandBreakpointHandler.AddBreakpoint(commandContent),
                _ => _gdbIo.GenerateUnsupportedResponse()
            };
            if (response != null) {
                _gdbIo.SendResponse(response);
            }
        } finally {
            if (_gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd) {
                pauseHandler.RequestResume();
            }
        }
    }

    private string Detach() {
        _isConnected = false;
        _gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd = true;
        return _gdbIo.GenerateResponse("");
    }

    private string HandleThreadALive() {
        return _gdbIo.GenerateResponse("OK");
    }

    private string Kill() {
        _machine.Cpu.IsRunning = false;
        return Detach();
    }

    private Tuple<string, object> ParseSupportedQuery(string item) {
        Tuple<string, object> res;
        if (item.EndsWith("+")) {
            res = Tuple.Create(item[0..^1], (object)true);
        } else if (item.EndsWith("-")) {
            res = Tuple.Create(item[0..^1], (object)false);
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
            "MustReplyEmpty" => _gdbIo.GenerateResponse(""),
            "Cont?" => _gdbIo.GenerateResponse(""),
            _ => _gdbIo.GenerateUnsupportedResponse()
        };
    }

    private string QueryVariable(string command) {
        if (command.StartsWith("Supported:")) {
            string[] supportedRequestItems = command.Replace("Supported:", "").Split(";");
            var supportedRequest = supportedRequestItems
                .ToDictionary(x => ParseSupportedQuery(x))
                .ToDictionary(data => data.Key.Item1, data => data.Key.Item2);
            if (supportedRequest.TryGetValue("xmlRegisters", out object? value) == false || value.Equals("i386") == false) {
                return _gdbIo.GenerateUnsupportedResponse();
            }

            return _gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("L")) {
            string nextthread = command[4..];
            return _gdbIo.GenerateResponse($"qM011{nextthread}00000001");
        }

        if (command.StartsWith("P")) {
            return _gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("ThreadExtraInfo")) {
            return _gdbIo.GenerateMessageToDisplayResponse("spice86");
        }

        if (command.StartsWith("Rcmd")) {
            return _gdbCustomCommandsHandler.HandleCustomCommands(command);
        }

        if (command.StartsWith("Search")) {
            return _gdbCommandMemoryHandler.SearchMemory(command);
        }

        return command switch {
            // The remote server attached to an existing process.
            "Attached" => _gdbIo.GenerateResponse("1"),
            // Return the current thread ID.
            "C" => _gdbIo.GenerateResponse("QC1"),
            // Ask the stub if there is a trace experiment running right now. -> No trace has been run yet.
            "TStatus" => _gdbIo.GenerateResponse(""),
            "fThreadInfo" => _gdbIo.GenerateResponse("m1"),
            "sThreadInfo" => _gdbIo.GenerateResponse("l"),
            _ => _gdbIo.GenerateUnsupportedResponse(),
        };
    }

    private string ReasonHalted() {
        return _gdbIo.GenerateResponse("S05");
    }

    private string SetThreadContext() {
        return _gdbIo.GenerateResponse("OK");
    }
}