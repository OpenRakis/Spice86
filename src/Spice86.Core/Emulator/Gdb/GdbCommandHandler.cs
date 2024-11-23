namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using System;
using System.Linq;

/// <summary>
/// The class responsible for answering to custom GDB commands.
/// </summary>
public class GdbCommandHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbCommandBreakpointHandler _gdbCommandBreakpointHandler;
    private readonly GdbCommandMemoryHandler _gdbCommandMemoryHandler;
    private readonly GdbCommandRegisterHandler _gdbCommandRegisterHandler;
    private readonly GdbCustomCommandsHandler _gdbCustomCommandsHandler;
    private readonly GdbIo _gdbIo;
    private readonly IPauseHandler _pauseHandler;
    private readonly State _state;
    private readonly FunctionHandler _functionHandler;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;

    /// <summary>
    /// Constructs a new instance of <see cref="GdbCommandHandler"/>
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="pauseHandler">The class that enables us to pause the emulator.</param>
    /// <param name="emulatorBreakpointsManager">The class used to store and retrieve breakpoints.</param>
    /// <param name="callbackHandler">The class that stores callbacks as machine code instructions and is responsible for calling our C# handlers.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="functionHandler">The class that handles function calls at the machine code level.</param>
    /// <param name="gdbIo">The GDB I/O handler.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The configuration object containing GDB settings.</param>
    public GdbCommandHandler(IMemory memory, Cpu cpu, State state, IPauseHandler pauseHandler,
        EmulatorBreakpointsManager emulatorBreakpointsManager, CallbackHandler callbackHandler, ExecutionFlowRecorder executionFlowRecorder,
        FunctionHandler functionHandler, GdbIo gdbIo, ILoggerService loggerService, Configuration configuration) {
        _loggerService = loggerService;
        _state = state;
        _gdbIo = gdbIo;
        _functionHandler = functionHandler;
        _executionFlowRecorder = executionFlowRecorder;
        _pauseHandler = pauseHandler;
        _gdbCommandRegisterHandler = new GdbCommandRegisterHandler(_state, gdbIo, _loggerService);
        _gdbCommandMemoryHandler = new GdbCommandMemoryHandler(memory, gdbIo, _loggerService);
        _gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(emulatorBreakpointsManager, pauseHandler, gdbIo, _loggerService);
        _gdbCustomCommandsHandler = new GdbCustomCommandsHandler(configuration, memory, cpu, callbackHandler, executionFlowRecorder, emulatorBreakpointsManager, gdbIo,
            _loggerService,
            _gdbCommandBreakpointHandler.OnBreakPointReached, configuration.RecordedDataDirectory);
    }

    /// <summary>
    /// Are we connected to the GDB client or not.
    /// </summary>
    public bool IsConnected { get; private set; } = true;

    internal void PauseEmulator() {
        _gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd = false;
        _pauseHandler.RequestPause("To wait for a client to connect");
    }

    /// <summary>
    /// Executes a single CPU instruction.
    /// </summary>
    public void Step() => _gdbCommandBreakpointHandler.Step();

    /// <summary>
    /// Runs a custom GDB command.
    /// </summary>
    /// <param name="command">The custom GDB command string</param>
    public void RunCommand(string command) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Received command {Command}", command);
        }
        char first = command[0];
        string commandContent = command[1..];
        if (!_pauseHandler.IsPaused) {
            _pauseHandler.RequestPause("to process Gdb command");
        }
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
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Responded with {Response}", response);
            }
            if (response != null) {
                _gdbIo.SendResponse(response);
            }
        } finally {
            if (_gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd) {
                _pauseHandler.Resume();
            }
        }
    }

    private string Detach() {
        IsConnected = false;
        _gdbCommandBreakpointHandler.ResumeEmulatorOnCommandEnd = true;
        return _gdbIo.GenerateResponse("");
    }

    private string HandleThreadALive() {
        return _gdbIo.GenerateResponse("OK");
    }

    private string Kill() {
        _state.IsRunning = false;
        return Detach();
    }

    private Tuple<string, object> ParseSupportedQuery(string item) {
        Tuple<string, object> res;
        if (item.EndsWith('+')) {
            res = Tuple.Create(item[0..^1], (object)true);
        } else if (item.EndsWith('-')) {
            res = Tuple.Create(item[0..^1], (object)false);
        } else {
            string[] split = item.Split('=');
            res = Tuple.Create(split[0], new object());
            if (split.Length == 2) {
                res = Tuple.Create(split[0], (object)split[1]);
            }
        }

        return res;
    }

    private string ProcessVPacket(string commandContent) {
        return commandContent switch {
            "MustReplyEmpty" => _gdbIo.GenerateResponse(""),
            "Cont?" => _gdbIo.GenerateResponse(""),
            _ => _gdbIo.GenerateUnsupportedResponse()
        };
    }

    private string QueryVariable(string command) {
        if (command.StartsWith("Supported:")) {
            string[] supportedRequestItems = command.Replace("Supported:", "").Split(";");
            Dictionary<string, object> supportedRequest = supportedRequestItems
                .ToDictionary(x => ParseSupportedQuery(x))
                .ToDictionary(data => data.Key.Item1, data => data.Key.Item2);
            if (supportedRequest.TryGetValue("xmlRegisters", out object? value) == false || value.Equals("i386") == false) {
                return _gdbIo.GenerateUnsupportedResponse();
            }

            return _gdbIo.GenerateResponse("");
        }

        if (command.StartsWith('L')) {
            string nextthread = command[4..];
            return _gdbIo.GenerateResponse($"qM011{nextthread}00000001");
        }

        if (command.StartsWith('P')) {
            return _gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("ThreadExtraInfo")) {
            return _gdbIo.GenerateMessageToDisplayResponse("spice86");
        }

        if (command.StartsWith("Rcmd")) {
            return _gdbCustomCommandsHandler.HandleCustomCommands(_executionFlowRecorder, _functionHandler, command);
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