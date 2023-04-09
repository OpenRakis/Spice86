using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Gdb;

using Serilog;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

public class GdbCommandMemoryHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbFormatter _gdbFormatter = new();
    private readonly GdbIo _gdbIo;
    private readonly Machine _machine;

    public GdbCommandMemoryHandler(GdbIo gdbIo, Machine machine, ILoggerService loggerService) {
        _loggerService = loggerService;
        _gdbIo = gdbIo;
        _machine = machine;
    }

    public string ReadMemory(string commandContent) {
        try {
            string[] commandContentSplit = commandContent.Split(",");
            uint address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            uint length = 1;
            if (commandContentSplit.Length > 1) {
                length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            }

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("Reading memory at address {Address} for a length of {Length}", address, length);
            }
            Memory memory = _machine.Memory;
            int memorySize = memory.Size;
            StringBuilder response = new StringBuilder((int)length * 2);
            for (long i = 0; i < length; i++) {
                long readAddress = address + i;
                if (readAddress >= memorySize) {
                    break;
                }

                byte b = memory.GetUint8((uint)readAddress);
                string value = _gdbFormatter.FormatValueAsHex8(b);
                response.Append(value);
            }

            return _gdbIo.GenerateResponse(response.ToString());
        } catch (FormatException nfe) {
            nfe.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Memory read requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }

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
        Memory memory = _machine.Memory;
        uint? address = memory.SearchValue(start, (int)end, patternBytesList);
        if (address == null) {
            return _gdbIo.GenerateResponse("0");
        }

        return _gdbIo.GenerateResponse("1," + _gdbFormatter.FormatValueAsHex32(address.Value));
    }

    public string WriteMemory(string commandContent) {
        try {
            string[] commandContentSplit = commandContent.Split("[,:]");
            uint address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            uint length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            byte[] data = ConvertUtils.HexToByteArray(commandContentSplit[2]);
            if (length != data.Length) {
                return _gdbIo.GenerateResponse("E01");
            }

            Memory memory = _machine.Memory;
            if (address + length > memory.Size) {
                return _gdbIo.GenerateResponse("E02");
            }

            memory.LoadData(address, data);
            return _gdbIo.GenerateResponse("OK");
        } catch (FormatException nfe) {
            nfe.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Memory write requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }
}