namespace Spice86.Emulator.Memory;

using Spice86.Emulator.Devices;
using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;


/// <summary>
/// Provides the basic services of an Intel 8237 DMA controller.
/// </summary>
public sealed class DmaController : IInputPort, IOutputPort {
    private static readonly int[] AllPorts = new int[] { 0x87, 0x00, 0x01, 0x83, 0x02, 0x03, 0x81, 0x04, 0x05, 0x82, 0x06, 0x07, 0x8F, 0xC0, 0xC2, 0x8B, 0xC4, 0xC6, 0x89, 0xC8, 0xCA, 0x8A, 0xCC, 0xCE };

    private const int ModeRegister8 = 0x0B;
    private const int ModeRegister16 = 0xD6;
    private const int MaskRegister8 = 0x0A;
    private const int MaskRegister16 = 0xD4;
    private const int AutoInitFlag = 1 << 4;

    private readonly List<DmaChannel> channels = new(8);

    internal DmaController() {
        for (int i = 0; i < 8; i++) {
            var channel = new DmaChannel();
            channels.Add(channel);
        }

        Channels = new ReadOnlyCollection<DmaChannel>(channels);
    }

    /// <summary>
    /// Gets the channels on the DMA controller.
    /// </summary>
    public ReadOnlyCollection<DmaChannel> Channels { get; }

    IEnumerable<int> IInputPort.InputPorts => Array.AsReadOnly(AllPorts);
    IEnumerable<int> IOutputPort.OutputPorts {
        get {
            var ports = new List<int>(AllPorts)
            {
                ModeRegister8,
                ModeRegister16,
                MaskRegister8,
                MaskRegister16
            };

            return ports.AsReadOnly();
        }
    }

    byte IInputPort.ReadByte(int port) => GetPortValue(port);
    ushort IInputPort.ReadWord(int port) => GetPortValue(port);
    void IOutputPort.WriteByte(int port, byte value) {
        switch (port) {
            case ModeRegister8:
                SetChannelMode(channels[value & 3], value);
                break;

            case ModeRegister16:
                SetChannelMode(channels[(value & 3) + 4], value);
                break;

            case MaskRegister8:
                channels[value & 3].IsMasked = (value & 4) != 0;
                break;

            case MaskRegister16:
                channels[(value & 3) + 4].IsMasked = (value & 4) != 0;
                break;

            default:
                SetPortValue(port, value);
                break;
        }
    }
    void IOutputPort.WriteWord(int port, ushort value) {
        int index = Array.IndexOf(AllPorts, port);
        if (index < 0)
            throw new ArgumentException("Invalid port.");

        switch (index % 3) {
            case 0:
                channels[index / 3].Page = (byte)value;
                break;

            case 1:
                channels[index / 3].Address = value;
                break;

            case 2:
                channels[index / 3].Count = value;
                channels[index / 3].TransferBytesRemaining = value + 1;
                break;
        }
    }

    /// <summary>
    /// Sets DMA channel mode information.
    /// </summary>
    /// <param name="channel">Channel whose mode is to be set.</param>
    /// <param name="value">Flags specifying channel's new mode information.</param>
    private static void SetChannelMode(DmaChannel channel, int value) {
        if ((value & AutoInitFlag) != 0)
            channel.TransferMode = DmaTransferMode.AutoInitialize;
        else
            channel.TransferMode = DmaTransferMode.SingleCycle;
    }
    /// <summary>
    /// Returns the value from a DMA channel port.
    /// </summary>
    /// <param name="port">Port to return value for.</param>
    /// <returns>Value of specified port.</returns>
    private byte GetPortValue(int port) {
        int index = Array.IndexOf(AllPorts, port);
        if (index < 0)
            throw new ArgumentException("Invalid port.");

        return (index % 3) switch {
            0 => channels[index / 3].Page,
            1 => channels[index / 3].ReadAddressByte(),
            2 => channels[index / 3].ReadCountByte(),
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
        if (index < 0)
            throw new ArgumentException("Invalid port.");

        switch (index % 3) {
            case 0:
                channels[index / 3].Page = value;
                break;

            case 1:
                channels[index / 3].WriteAddressByte(value);
                break;

            case 2:
                channels[index / 3].WriteCountByte(value);
                break;
        }
    }
}
