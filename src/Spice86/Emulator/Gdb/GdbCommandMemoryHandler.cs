namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Text;

public class GdbCommandMemoryHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbCommandMemoryHandler>();
    private GdbFormatter gdbFormatter = new GdbFormatter();
    private GdbIo gdbIo;
    private Machine machine;

    public GdbCommandMemoryHandler(GdbIo gdbIo, Machine machine) {
        this.gdbIo = gdbIo;
        this.machine = machine;
    }

    public string ReadMemory(string commandContent) {
        try {
            String[] commandContentSplit = commandContent.Split(",");
            long address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            long length = 1;
            if (commandContentSplit.Length > 1) {
                length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            }

            _logger.Information("Reading memory at address {@Address} for a length of {@Length}", address, length);
            Memory memory = machine.GetMemory();
            int memorySize = memory.GetSize();
            if (address < 0) {
                return gdbIo.GenerateResponse("");
            }

            StringBuilder response = new StringBuilder((int)length * 2);
            for (long i = 0; i < length; i++) {
                long readAddress = address + i;
                if (readAddress >= memorySize) {
                    break;
                }

                byte b = memory.GetUint8((int)readAddress);
                string value = gdbFormatter.FormatValueAsHex8(b);
                response.Append(value);
            }

            return gdbIo.GenerateResponse(response.ToString());
        } catch (FormatException nfe) {
            _logger.Error(nfe, "Memory read requested but could not understand the request {@CommandContent}", commandContent);
            return gdbIo.GenerateUnsupportedResponse();
        }
    }

    public string SearchMemory(string command) {
        String[] parameters = command.Replace("Search:memory:", "").Split(";");
        long start = ConvertUtils.ParseHex32(parameters[0]);
        long end = ConvertUtils.ParseHex32(parameters[1]);

        // read the bytes from the raw command as GDB does not send them as hex
        List<Byte> rawCommand = gdbIo.GetRawCommand();

        // Extract the original hex sent by GDB, read from
        // 3: +$q
        // variable: header
        // 2: ;
        // variable 2 hex strings
        int patternStartIndex = 3 + "Search:memory:".Length + 2 + parameters[0].Length + parameters[1].Length;
        List<Byte> patternBytesList = rawCommand.GetRange(patternStartIndex, rawCommand.Count - 1);
        Memory memory = machine.GetMemory();
        int? address = memory.SearchValue((int)start, (int)end, patternBytesList);
        if (address == null) {
            return gdbIo.GenerateResponse("0");
        }

        return gdbIo.GenerateResponse("1," + gdbFormatter.FormatValueAsHex32(address.Value));
    }

    public string WriteMemory(string commandContent) {
        try {
            String[] commandContentSplit = commandContent.Split("[,:]");
            long address = ConvertUtils.ParseHex32(commandContentSplit[0]);
            long length = ConvertUtils.ParseHex32(commandContentSplit[1]);
            byte[] data = ConvertUtils.HexToByteArray(commandContentSplit[2]);
            if (length != data.Length) {
                return gdbIo.GenerateResponse("E01");
            }

            Memory memory = machine.GetMemory();
            if (address + length > memory.GetSize()) {
                return gdbIo.GenerateResponse("E02");
            }

            memory.LoadData((int)address, data);
            return gdbIo.GenerateResponse("OK");
        } catch (FormatException nfe) {
            _logger.Error(nfe, "Memory write requested but could not understand the request {@CommandContent}", commandContent);
            return gdbIo.GenerateUnsupportedResponse();
        }
    }
}