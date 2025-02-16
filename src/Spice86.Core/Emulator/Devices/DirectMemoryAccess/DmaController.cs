namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Collections.Frozen;
using System.Collections.ObjectModel;

/// <summary>
/// Provides the basic services of an Intel 8237 DMA controller.
/// </summary>
public class DmaController : DefaultIOPortHandler {
    private const int ModeRegister8 = 0x0B;
    private const int ModeRegister16 = 0xD6;
    private const int MaskRegister8 = 0x0A;
    private const int MaskRegister16 = 0xD4;
    private const int AutoInitFlag = 1 << 4;
    private const int ClearBytePointerFlipFlop = 0xC;
    private readonly List<DmaChannel> _dmaDeviceChannels = new();
    private readonly List<DmaChannel> _channels = new(8);
    private readonly IMemory _memory;

    private static readonly FrozenSet<int> _otherOutputPorts = new int[] {
            ModeRegister8,
            ModeRegister16,
            MaskRegister8,
            MaskRegister16,
            ClearBytePointerFlipFlop}.ToFrozenSet();

    private static readonly int[] AllPorts = [
        0x87, 0x00, 0x01,
        0x83, 0x02, 0x03,
        0x81, 0x04, 0x05,
        0x82, 0x06, 0x07,
        0x8F, 0xC0, 0xC2,
        0x8B, 0xC4, 0xC6,
        0x89, 0xC8, 0xCA,
        0x8A, 0xCC, 0xCE
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="DmaController"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an IO port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DmaController(IMemory memory, State state, IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
        _memory = memory;
        for (int i = 0; i < 8; i++) {
            DmaChannel channel = new DmaChannel(_memory, state);
            _channels.Add(channel);
        }

        Channels = new ReadOnlyCollection<DmaChannel>(_channels);

        InitPortHandlers(ioPortDispatcher);
    }

    internal void PerformDmaTransfers() {
        foreach (DmaChannel channel in _dmaDeviceChannels) {
            channel.Transfer();
        }
    }

    internal void SetupDmaDeviceChannel(IDmaDevice8 dmaDevice) {
        if (dmaDevice.Channel < 0 || dmaDevice.Channel >= Channels.Count) {
            throw new ArgumentException("Invalid DMA channel on DMA device.");
        }
        Channels[dmaDevice.Channel].Device = dmaDevice;
        _dmaDeviceChannels.Add(Channels[dmaDevice.Channel]);
    }

    /// <summary>
    /// Gets the channels on the DMA controller.
    /// </summary>
    public ReadOnlyCollection<DmaChannel> Channels { get; }

    /// <summary>
    /// Gets the input ports for the DMA controller.
    /// </summary>
    public IReadOnlyList<int> InputPorts => AllPorts.AsReadOnly();

    /// <summary>
    /// Gets the output ports for the DMA controller.
    /// </summary>
    public IReadOnlyList<int> OutputPorts {
        get {
            List<int> ports = new List<int>(AllPorts)
            {
                ModeRegister8,
                ModeRegister16,
                MaskRegister8,
                MaskRegister16
            };

            return ports.AsReadOnly();
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        foreach (int value in OutputPorts) {
            ioPortDispatcher.AddIOPortHandler(value, this);
        }
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        return GetPortValue(port);
    }

    /// <inheritdoc/>
    public override ushort ReadWord(ushort port) {
        return GetPortValue(port);
    }

    /// <inheritdoc/>
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case ModeRegister8:
                SetChannelMode(_channels[value & 3], value);
                break;

            case ModeRegister16:
                SetChannelMode(_channels[(value & 3) + 4], value);
                break;

            case MaskRegister8:
                _channels[value & 3].IsMasked = (value & 4) != 0;
                break;

            case MaskRegister16:
                _channels[(value & 3) + 4].IsMasked = (value & 4) != 0;
                break;

            case ClearBytePointerFlipFlop:
                // clear byte pointer flip-flop.
                // Any write clears the flip-flop so that the next write to any of the 16-bit registers is decoded as the low byte.
                // The next is the high byte, then next is low, etc.
                break;

            default:
                SetPortValue(port, value);
                break;
        }
    }

    /// <inheritdoc/>
    public override void WriteWord(ushort port, ushort value) {
        int index = Array.IndexOf(AllPorts, port);

        switch (index % 3) {
            case 0:
                _channels[index / 3].Page = (byte)value;
                break;

            case 1:
                _channels[index / 3].Address = value;
                break;

            case 2:
                _channels[index / 3].Count = value;
                _channels[index / 3].TransferBytesRemaining = value + 1;
                break;
            default:
                base.WriteWord(port, value);
                break;
        }
    }

    /// <summary>
    /// Sets DMA channel mode information.
    /// </summary>
    /// <param name="channel">Channel whose mode is to be set.</param>
    /// <param name="value">Flags specifying channel's new mode information.</param>
    private static void SetChannelMode(DmaChannel channel, int value) {
        if ((value & AutoInitFlag) != 0) {
            channel.TransferMode = DmaTransferMode.AutoInitialize;
        } else {
            channel.TransferMode = DmaTransferMode.SingleCycle;
        }
    }

    /// <summary>
    /// Returns the value from a DMA channel port.
    /// </summary>
    /// <param name="port">Port to return value for.</param>
    /// <returns>Value of specified port.</returns>
    private byte GetPortValue(int port) {
        int index = Array.IndexOf(AllPorts, port);

        return (index % 3) switch {
            0 => _channels[index / 3].Page,
            1 => _channels[index / 3].ReadAddressByte(),
            2 => _channels[index / 3].ReadCountByte(),
            _ => 0
        };
    }

    /// <summary>
    /// Writes a value to a specified DMA channel port.
    /// </summary>
    /// <param name="port">Port to write value to.</param>
    /// <param name="value">Value to write.</param>
    private void SetPortValue(int port, byte value) {
        int index = Array.IndexOf(AllPorts, port);

        switch (index % 3) {
            case 0:
                _channels[index / 3].Page = value;
                break;

            case 1:
                _channels[index / 3].WriteAddressByte(value);
                break;

            case 2:
                _channels[index / 3].WriteCountByte(value);
                break;
        }
    }
}