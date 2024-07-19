namespace Spice86.Core.Emulator.Gdb;

using System.Diagnostics;
using System.Text;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Handles GDB memory-related commands such as reading and writing memory, and searching for patterns in memory.
/// </summary>
public class GdbCommandMemoryHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbFormatter _gdbFormatter;
    private readonly GdbIo _gdbIo;
    private readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GdbCommandMemoryHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="gdbFormatter">The class that formats values in a way compatible with GDB.</param>
    /// <param name="gdbIo">The GDB I/O handler.</param>
    /// <param name="loggerService">The logger service.</param>
    public GdbCommandMemoryHandler(IMemory memory, GdbFormatter gdbFormatter,  GdbIo gdbIo, ILoggerService loggerService) {
        _loggerService = loggerService;
        _gdbFormatter = gdbFormatter;
        _memory = memory;
        _gdbIo = gdbIo;
    }

    /// <summary>
    /// Reads memory from the machine being debugged.
    /// </summary>
    /// <param name="commandContent">The command content specifying the memory location and length to read.</param>
    /// <returns>The response to send back to GDB.</returns>
    public string ReadMemory(string commandContent) {
        try {
            string[] commandContentSplit = commandContent.Split(",");
            uint address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            uint length = 1;
            if (commandContentSplit.Length > 1) {
                length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            }

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Reading memory at address {Address} for a length of {Length}", address, length);
            }
            uint memorySize = _memory.Length;
            StringBuilder response = new StringBuilder((int)length * 2);
            for (long i = 0; i < length; i++) {
                long readAddress = address + i;
                if (readAddress >= memorySize) {
                    break;
                }

                byte b = _memory.UInt8[(uint)readAddress];
                string value = _gdbFormatter.FormatValueAsHex8(b);
                response.Append(value);
            }

            return _gdbIo.GenerateResponse(response.ToString());
        } catch (FormatException nfe) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Memory read requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }

    /// <summary>
    /// Searches for a pattern in memory.
    /// </summary>
    /// <param name="command">The command containing the memory location range and pattern to search for.</param>
    /// <returns>The response to send back to GDB.</returns>
    public string SearchMemory(string command) {
        string[] parameters = command.Replace("Search:memory:", "").Split(";");
        uint start = ConvertUtils.ParseHex32(parameters[0]);
        uint end = ConvertUtils.ParseHex32(parameters[1]);

        // read the bytes from the raw command as GDB does not send them as hex
        List<byte> rawCommand = _gdbIo.RawCommand;

        // Extract the original hex sent by GDB, read from
        // 3: +$q
        // variable: header
        // 2: ;
        // variable 2 hex strings
        int patternStartIndex = 3 + "Search:memory:".Length + 2 + parameters[0].Length + parameters[1].Length;
        List<byte> patternBytesList = rawCommand.GetRange(patternStartIndex, rawCommand.Count - 1);
        uint? address = _memory.SearchValue(start, (int)end, patternBytesList);
        if (address == null) {
            return _gdbIo.GenerateResponse("0");
        }

        return _gdbIo.GenerateResponse($"1,{_gdbFormatter.FormatValueAsHex32(address.Value)}");
    }

    /// <summary>
    /// Writes data to the specified memory address.
    /// </summary>
    /// <param name="commandContent">The command content in the format "address,length:data".</param>
    /// <returns>The GDB response.</returns>
    public string WriteMemory(string commandContent) {
        try {
            string[] commandContentSplit = commandContent.Split("[,:]");
            uint address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            uint length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            byte[] data = ConvertUtils.HexToByteArray(commandContentSplit[2]);
            if (length != data.Length) {
                return _gdbIo.GenerateResponse("E01");
            }

            if (address + length > _memory.Length) {
                return _gdbIo.GenerateResponse("E02");
            }

            _memory.LoadData(address, data);
            return _gdbIo.GenerateResponse("OK");
        } catch (FormatException nfe) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Memory write requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }
}