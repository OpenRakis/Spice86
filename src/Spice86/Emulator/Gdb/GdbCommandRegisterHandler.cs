namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;
using Spice86.Utils;

using System;
using System.Text;

public class GdbCommandRegisterHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbCommandRegisterHandler>();
    private readonly GdbFormatter _gdbFormatter = new();
    private readonly GdbIo _gdbIo;
    private readonly Machine _machine;

    public GdbCommandRegisterHandler(GdbIo gdbIo, Machine machine) {
        _gdbIo = gdbIo;
        _machine = machine;
    }

    public string ReadAllRegisters() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Reading all registers");
        }
        StringBuilder response = new(2 * 4 * 16);
        for (int i = 0; i < 16; i++) {
            string regValue = _gdbFormatter.FormatValueAsHex32(GetRegisterValue(i));
            response.Append(regValue);
        }

        return _gdbIo.GenerateResponse(response.ToString());
    }

    public string ReadRegister(string commandContent) {
        try {
            long index = ConvertUtils.ParseHex32(commandContent);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Reading register {@RegisterIndex}", index);
            }
            return _gdbIo.GenerateResponse(_gdbFormatter.FormatValueAsHex32(GetRegisterValue((int)index)));
        } catch (FormatException nfe) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(nfe, "Register read requested but could not understand the request {@CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }

    public string WriteAllRegisters(string commandContent) {
        try {
            byte[] data = ConvertUtils.HexToByteArray(commandContent);
            for (int i = 0; i < data.Length; i += 4) {
                long value = ConvertUtils.BytesToInt32(data, i);
                SetRegisterValue(i / 4, (ushort)value);
            }

            return _gdbIo.GenerateResponse("OK");
        } catch (FormatException nfe) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(nfe, "Register write requested but could not understand the request {@CommandContent}", commandContent);
            }
            return _gdbIo.GenerateUnsupportedResponse();
        }
    }

    public string WriteRegister(string commandContent) {
        String[] split = commandContent.Split("=");
        int registerIndex = (int)ConvertUtils.ParseHex32(split[0]);
        uint registerValue = ConvertUtils.Swap32(ConvertUtils.ParseHex32(split[1]));
        SetRegisterValue(registerIndex, (ushort)registerValue);
        return _gdbIo.GenerateResponse("OK");
    }

    private uint GetRegisterValue(int regIndex) {
        State state = _machine.GetCpu().GetState();
        if (regIndex < 8) {
            return state.GetRegisters().GetRegister(regIndex);
        }

        if (regIndex == 8) {
            return state.GetIpPhysicalAddress();
        }

        if (regIndex == 9) {
            return state.GetFlags().FlagRegister;
        }

        if (regIndex < 16) {
            return state.GetSegmentRegisters().GetRegister(GetSegmentRegisterIndex(regIndex));
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
        State state = _machine.GetCpu().GetState();
        if (regIndex < 8) {
            state.GetRegisters().SetRegister(regIndex, value);
        } else if (regIndex == 8) {
            state.SetIP(value);
        } else if (regIndex == 9) {
            state.GetFlags().SetFlagRegister(value);
        } else if (regIndex < 16) {
            state.GetSegmentRegisters().SetRegister(GetSegmentRegisterIndex(regIndex), value);
        }
    }
}