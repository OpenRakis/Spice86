namespace Spice86.Core.Emulator.Gdb;

using System.Diagnostics;
using System.Text;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Handles GDB commands related to reading and writing CPU registers.
/// </summary>
public class GdbCommandRegisterHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbFormatter _gdbFormatter = new();
    private readonly GdbIo _gdbIo;
    private readonly Machine _machine;

    /// <summary>
    /// Initializes a new instance of the GdbCommandRegisterHandler class
    /// </summary>
    /// <param name="gdbIo">The GdbIo object to use for communication with GDB.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The ILoggerService implementation.</param>
    public GdbCommandRegisterHandler(GdbIo gdbIo, Machine machine, ILoggerService loggerService) {
        _loggerService = loggerService;
        _gdbIo = gdbIo;
        _machine = machine;
    }

    /// <summary>
    /// Handles the GDB command to read all registers and returns their values as a response string.
    /// </summary>
    /// <returns>A string containing the response to the GDB command.</returns>
    public string ReadAllRegisters() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading all registers");
        }
        StringBuilder response = new(2 * 4 * 16);
        for (int i = 0; i < 16; i++) {
            string regValue = _gdbFormatter.FormatValueAsHex32(GetRegisterValue(i));
            response.Append(regValue);
        }

        return _gdbIo.GenerateResponse(response.ToString());
    }

    /// <summary>
    /// Reads the value of a register specified by the given command content in hex format.
    /// </summary>
    /// <param name="commandContent">The content of the command specifying the register index in hex format.</param>
    /// <returns>The hex representation of the value of the specified register.</returns>
    public string ReadRegister(string commandContent) {
        try {
            long index = ConvertUtils.ParseHex32(commandContent);
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Reading register {RegisterIndex}", index);
            }
            return _gdbIo.GenerateResponse(_gdbFormatter.FormatValueAsHex32(GetRegisterValue((int)index)));
        } catch (FormatException nfe) {
            nfe.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Register read requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }

    /// <summary>
    /// Writes the values of all registers specified by the given command content in hex format.
    /// </summary>
    /// <param name="commandContent">The content of the command specifying the values of all registers in hex format.</param>
    /// <returns>A response indicating whether the write operation was successful.</returns>
    public string WriteAllRegisters(string commandContent) {
        try {
            byte[] data = ConvertUtils.HexToByteArray(commandContent);
            for (int i = 0; i < data.Length; i += 4) {
                long value = ConvertUtils.BytesToInt32(data, i);
                SetRegisterValue(i / 4, (ushort)value);
            }

            return _gdbIo.GenerateResponse("OK");
        } catch (FormatException nfe) {
            nfe.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Register write requested but could not understand the request {CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }
    
    /// <summary>
    /// Handles the GDB command to write a value to a specific register.
    /// </summary>
    /// <param name="commandContent">The content of the GDB command containing the register index and value to be written.</param>
    /// <returns>A string containing the response to the GDB command.</returns>
    public string WriteRegister(string commandContent) {
        string[] split = commandContent.Split("=");
        int registerIndex = (int)ConvertUtils.ParseHex32(split[0]);
        uint registerValue = ConvertUtils.Swap32(ConvertUtils.ParseHex32(split[1]));
        SetRegisterValue(registerIndex, (ushort)registerValue);
        return _gdbIo.GenerateResponse("OK");
    }

    private uint GetRegisterValue(int regIndex) {
        State state = _machine.Cpu.State;
        if (regIndex < 8) {
            return state.Registers.GetRegister16(regIndex);
        }

        if (regIndex == 8) {
            uint value = state.IpPhysicalAddress;
            Debug.WriteLine($"{value:X}");
            return value;
        }

        if (regIndex == 9) {
            return state.Flags.FlagRegister;
        }

        if (regIndex < 16) {
            return state.SegmentRegisters.GetRegister16(GetSegmentRegisterIndex(regIndex));
        }

        return 0;
    }

    private int GetSegmentRegisterIndex(int gdbRegisterIndex) {
        int registerIndex = gdbRegisterIndex - 10;
        if (registerIndex < 3) {
            return registerIndex + 1;
        }

        if (registerIndex == 3) {
            return 0;
        }

        return registerIndex;
    }

    private void SetRegisterValue(int regIndex, ushort value) {
        State state = _machine.Cpu.State;
        if (regIndex < 8) {
            state.Registers.SetRegister16(regIndex, value);
        } else if (regIndex == 8) {
            state.IP = value;
        } else if (regIndex == 9) {
            state.Flags.FlagRegister = value;
        } else if (regIndex < 16) {
            state.SegmentRegisters.SetRegister16(GetSegmentRegisterIndex(regIndex), value);
        }
    }
}