namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Provides the basic services of an Intel 8237 DMA controller.
/// </summary>
public sealed class DmaController : DefaultIOPortHandler {
    private const int ModeRegister8 = 0x0B;
    private const int ModeRegister16 = 0xD6;
    private const int MaskRegister8 = 0x0A;
    private const int MaskRegister16 = 0xD4;
    private const int AutoInitFlag = 1 << 4;
    private const int ClearBytePointerFlipFlop = 0xC;

    private static readonly int[] _otherOutputPorts = new[] {
            ModeRegister8,
            ModeRegister16,
            MaskRegister8,
            MaskRegister16,
            ClearBytePointerFlipFlop};

    private static readonly int[] AllPorts = new[] { 0x87, 0x00, 0x01, 0x83, 0x02, 0x03, 0x81, 0x04, 0x05, 0x82, 0x06, 0x07, 0x8F, 0xC0, 0xC2, 0x8B, 0xC4, 0xC6, 0x89, 0xC8, 0xCA, 0x8A, 0xCC, 0xCE };

    private readonly List<DmaChannel> _channels = new(8);
    
    /// <summary>
    /// Initializes a new instance of the <see cref="DmaController"/> class.
    /// </summary>
    /// <param name="machine">Machine where the DMA controller is located.</param>
    /// <param name="configuration">Configuration of the machine where the DMA controller is located.</param>
    /// <param name="loggerService">Service used to log information about the DMA controller.</param>
    public DmaController(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        for (int i = 0; i < 8; i++) {
            DmaChannel channel = new DmaChannel();
            _channels.Add(channel);
        }

        Channels = new ReadOnlyCollection<DmaChannel>(_channels);
    }

    /// <summary>
    /// Gets the channels on the DMA controller.
    /// </summary>
    public ReadOnlyCollection<DmaChannel> Channels { get; }

    /// <summary>
    /// Gets the input ports for the DMA controller.
    /// </summary>
    public IEnumerable<int> InputPorts => Array.AsReadOnly(AllPorts);

    /// <summary>
    /// Gets the output ports for the DMA controller.
    /// </summary>
    public IEnumerable<int> OutputPorts {
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

    /// <inheritdoc/>
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        foreach (int value in OutputPorts) {
            ioPortDispatcher.AddIOPortHandler(value, this);
        }
    }

    /// <inheritdoc/>
    public override byte ReadByte(int port) {
        return GetPortValue(port);
    }

    /// <inheritdoc/>
    public override ushort ReadWord(int port) {
        return GetPortValue(port);
    }

    /// <inheritdoc/>
    public override void WriteByte(int port, byte value) {
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

            default:
                SetPortValue(port, value);
                break;
        }
    }

    /// <inheritdoc/>
    public override void WriteWord(int port, ushort value) {
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
        if (index < 0) {
            throw new ArgumentException("Invalid port.");
        }

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
        if (index < 0) {
            throw new ArgumentException("Invalid port.");
        }

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