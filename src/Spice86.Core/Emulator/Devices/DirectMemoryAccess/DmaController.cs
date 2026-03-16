namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
///     Models a single Intel 8237-compatible DMA controller that manages four DMA channels.
/// </summary>
internal sealed class DmaController {
    private readonly DmaChannel[] _channels = new DmaChannel[4];
    private readonly byte _index;
    private readonly ILoggerService _logger;

    private bool _flipFlop;

    /// <summary>
    ///     Initializes a DMA controller instance and the four physical channels it owns.
    /// </summary>
    /// <param name="controllerIndex">Index of the controller (0 for primary, 1 for secondary).</param>
    /// <param name="memory">The emulator memory abstraction used by the channels.</param>
    /// <param name="logger">Logging facility for diagnostic output.</param>
    /// <param name="wrappingMask">Mask applied when wrapping physical addresses handled by this controller.</param>
    public DmaController(byte controllerIndex, IMemory memory, ILoggerService logger, uint wrappingMask = 0xFFFF) {
        Debug.Assert(controllerIndex is 0 or 1);
        _index = controllerIndex;
        _logger = logger;

        for (byte i = 0; i < _channels.Length; i++) {
            byte channelNumber = (byte)((controllerIndex * 4) + i);
            bool is16Bit = controllerIndex == 1;
            _channels[i] = new DmaChannel(channelNumber, is16Bit, memory, logger, wrappingMask);
        }

        _logger.Debug("DMA[{Index}]: Controller initialised", _index);
    }

    /// <summary>
    ///     Retrieves the controller-local DMA channel for the supplied index (0..3).
    /// </summary>
    public DmaChannel? GetChannel(byte localChannel) {
        return localChannel < _channels.Length ? _channels[localChannel] : null;
    }

    /// <summary>
    ///     Resets the specified DMA channel to its default register state.
    /// </summary>
    public void ResetChannel(byte localChannel) {
        DmaChannel? channel = GetChannel(localChannel);
        if (channel is null) {
            return;
        }

        _logger.Debug("DMA[{Index}]: Resetting channel {Channel}", _index, localChannel);
        channel.Reset();
    }

    /// <summary>
    ///     Handles a write to one of the controller's registers and dispatches it to the appropriate channel logic.
    /// </summary>
    public void WriteRegister(byte reg, byte value) {
        _logger.Verbose("DMA[{Index}]: Write register 0x{Register:X2} value 0x{Value:X2}", _index, reg, value);

        switch (reg) {
            case 0x0:
            case 0x2:
            case 0x4:
            case 0x6:
                ChannelSetCurrentAddress(reg, value);
                break;

            case 0x1:
            case 0x3:
            case 0x5:
            case 0x7:
                ChannelSetCurrentCount(reg, value);
                break;

            case 0x8:
                // Command register unused.
                break;

            case 0x9:
                _logger.Warning("DMA: Unsupported memory-to-memory requested at controller {Index}", _index);
                // Request register / memory-to-memory. No-op.
                break;

            case 0xA:
                ChannelSetMask(value);
                break;

            case 0xB:
                ChannelSetMode(value);
                break;

            case 0xC:
                _flipFlop = false;
                _logger.Debug("DMA[{Index}]: Flip-flop reset", _index);
                break;
            case 0xD:
                ChannelClearAndMaskAll();
                break;

            case 0xE:
                ChannelUnmaskAll();
                break;

            // Write all mask bits in one operation
            case 0xF:
                ChannelSetMaskAll(value);
                break;

            default:
                _logger.Warning("DMA: Undefined write to controller {Index} register 0x{Register:X}", _index, reg);
                break;
        }
    }

    private void ChannelSetMaskAll(byte value) {
        _logger.Debug("DMA[{Index}]: Writing mask register with value 0x{Value:X2}", _index, value);

        foreach (DmaChannel channel in _channels) {
            bool mask = (value & 0x1) != 0;
            channel.SetMask(mask);
            _logger.Verbose("DMA[{Index}]: Channel {Channel} mask set to {Mask}", _index, channel.ChannelNumber, mask);
            value >>= 1;
        }
    }

    private void ChannelUnmaskAll() {
        _logger.Debug("DMA[{Index}]: Unmasking all channels", _index);
        foreach (DmaChannel channel in _channels) {
            channel.SetMask(false);
            _logger.Verbose("DMA[{Index}]: Channel {Channel} unmasked", _index, channel.ChannelNumber);
        }
    }

    private void ChannelClearAndMaskAll() {
        _logger.Debug("DMA[{Index}]: Clearing controller state and masking all channels", _index);

        foreach (DmaChannel channel in _channels) {
            channel.SetMask(true);
            channel.HasReachedTerminalCount = false;
            _logger.Verbose("DMA[{Index}]: Channel {Channel} masked during controller clear", _index,
                channel.ChannelNumber);
        }

        _flipFlop = false;
    }

    private void ChannelSetMode(byte value) {
        DmaChannel? channel = GetChannel((byte)(value & 0x3));
        if (channel is null) {
            return;
        }

        channel.SetMode(value);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            string addressMode = channel.IsIncremented ? "increment" : "decrement";
            _logger.Debug(
                "DMA[{Index}]: Channel {Channel} mode auto-init={AutoInit}, address={AddressMode}",
                _index,
                channel.ChannelNumber,
                channel.IsAutoiniting,
                addressMode);
        }
    }

    private void ChannelSetMask(byte value) {
        DmaChannel? channel = GetChannel((byte)(value & 0x3));
        if (channel is null) {
            return;
        }

        bool mask = (value & 0x4) != 0;
        channel.SetMask(mask);
        _logger.Debug("DMA[{Index}]: Channel {Channel} mask set to {Mask}", _index, channel.ChannelNumber, mask);
    }

    private void ChannelSetCurrentCount(byte reg, byte value) {
        DmaChannel? channel = GetChannel((byte)(reg >> 1));
        if (channel is null) {
            return;
        }

        _flipFlop = !_flipFlop;
        if (_flipFlop) {
            channel.BaseCount = (ushort)((channel.BaseCount & 0xFF00) | value);
            channel.CurrentCount = (ushort)((channel.CurrentCount & 0xFF00) | value);
        } else {
            channel.BaseCount = (ushort)((channel.BaseCount & 0x00FF) | (value << 8));
            channel.CurrentCount = (ushort)((channel.CurrentCount & 0x00FF) | (value << 8));
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug(
                "DMA[{Index}]: Channel {Channel} count set to base 0x{Base:X4}, current 0x{Current:X4}",
                _index,
                channel.ChannelNumber,
                channel.BaseCount,
                channel.CurrentCount);
        }
    }

    private void ChannelSetCurrentAddress(byte reg, byte value) {
        DmaChannel? channel = GetChannel((byte)(reg >> 1));
        if (channel is null) {
            return;
        }

        _flipFlop = !_flipFlop;
        if (_flipFlop) {
            channel.BaseAddress = (ushort)((channel.BaseAddress & 0xFF00) | value);
            channel.CurrentAddress = (channel.CurrentAddress & 0xFF00) | value;
        } else {
            channel.BaseAddress = (ushort)((channel.BaseAddress & 0x00FF) | (value << 8));
            channel.CurrentAddress = (channel.CurrentAddress & 0x00FF) | (uint)(value << 8);
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug(
                "DMA[{Index}]: Channel {Channel} address set to base 0x{Base:X4}, current 0x{Current:X4}",
                _index,
                channel.ChannelNumber,
                channel.BaseAddress,
                channel.CurrentAddress);
        }
    }

    /// <summary>
    ///     Reads the requested controller register and returns the corresponding state byte.
    /// </summary>
    public byte ReadRegister(byte reg) {
        switch (reg) {
            case 0x0:
            case 0x2:
            case 0x4:
            case 0x6:
                return ChannelGetCurrentAddress(reg);

            case 0x1:
            case 0x3:
            case 0x5:
            case 0x7:
                return ChannelGetCurrentCount(reg);

            case 0x8:
                return ChannelGetStatusRegisterAndClearTc();

            default:
                _logger.Warning("DMA: Undefined read from controller {Index} register 0x{Register:X}", _index, reg);
                return 0xFF;
        }
    }

    private byte ChannelGetStatusRegisterAndClearTc() {
        byte result = 0;
        for (byte i = 0; i < _channels.Length; i++) {
            DmaChannel channel = _channels[i];
            if (channel.HasReachedTerminalCount) {
                result |= (byte)(1 << i);
            }

            channel.HasReachedTerminalCount = false;
            if (channel.HasRaisedRequest) {
                result |= (byte)(1 << (4 + i));
            }
        }

        _logger.Verbose("DMA[{Index}]: Read status register -> 0x{Value:X2}", _index, result);
        return result;
    }

    private byte ChannelGetCurrentCount(byte reg) {
        DmaChannel? channel = GetChannel((byte)(reg >> 1));
        if (channel is null) {
            return 0;
        }

        _flipFlop = !_flipFlop;
        byte result = _flipFlop
            ? (byte)(channel.CurrentCount & 0xFF)
            : (byte)((channel.CurrentCount >> 8) & 0xFF);

        _logger.Verbose("DMA[{Index}]: Read count register 0x{Register:X2} -> 0x{Value:X2}", _index, reg, result);
        return result;
    }

    private byte ChannelGetCurrentAddress(byte reg) {
        DmaChannel? channel = GetChannel((byte)(reg >> 1));
        if (channel is null) {
            return 0;
        }

        _flipFlop = !_flipFlop;
        byte result = _flipFlop
            ? (byte)(channel.CurrentAddress & 0xFF)
            : (byte)((channel.CurrentAddress >> 8) & 0xFF);

        _logger.Verbose("DMA[{Index}]: Read address register 0x{Register:X2} -> 0x{Value:X2}", _index, reg, result);
        return result;
    }
}