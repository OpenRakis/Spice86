namespace Spice86.Core.Emulator.Devices.Cmos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates the MC146818 Real Time Clock (RTC) and CMOS RAM found in IBM PC/AT and compatible computers.
/// </summary>
public class Cmos : DefaultIOPortHandler {
    private const ushort AddressPort = 0x70;
    private const ushort DataPort = 0x71;
    
    private readonly byte[] _registers = new byte[0x40];
    private readonly DualPic _dualPic;
    
    private struct RtcTimer {
        public bool Enabled;
        public byte Divider;
        public double Delay;
        public bool Acknowledged;
        public double LastTrigger;
    }
    
    private RtcTimer _rtcTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cmos"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher.</param>
    /// <param name="dualPic">The dual PIC for interrupt handling.</param>
    /// <param name="failOnUnhandledPort">Whether to fail on unhandled ports.</param>
    /// <param name="loggerService">The logger service.</param>
    public Cmos(State state, IOPortDispatcher ioPortDispatcher, DualPic dualPic,
        bool failOnUnhandledPort, ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        
        // Initialize CMOS registers with default values
        _registers[0x0A] = 0x26; // Rate selection 32.768kHz base, divider 32 (1024Hz)
        _registers[0x0B] = 0x02; // 24-hour mode
        _registers[0x0D] = 0x80; // RTC power on
        
        // Initialize memory size values (640KB conventional memory)
        _registers[0x15] = 0x80;
        _registers[0x16] = 0x02;
        
        // Register I/O port handlers
        ioPortDispatcher.AddIOPortHandler(AddressPort, this);
        ioPortDispatcher.AddIOPortHandler(DataPort, this);
        
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("CMOS/RTC initialized");
        }
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case AddressPort:
                break;
            case DataPort:
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port == DataPort) {
        }
        
        return base.ReadByte(port);
    }

}