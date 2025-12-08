namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     Coordinates the primary and secondary DMA controllers and exposes ISA-compatible DMA behavior.
/// </summary>
public sealed class DmaBus : DefaultIOPortHandler {
    private static readonly Dictionary<ushort, byte> PageRegisterToChannel = new() {
        { 0x87, 0 }, // channel 0
        { 0x83, 1 }, // channel 1
        { 0x81, 2 }, // channel 2
        { 0x82, 3 }, // channel 3
        { 0x8F, 4 }, // channel 4 (cascade)
        { 0x8B, 5 }, // channel 5
        { 0x89, 6 }, // channel 6
        { 0x8A, 7 } // channel 7
    };

    private readonly DmaChannel[] _channels = new DmaChannel[8];
    private readonly ILoggerService _logger;
    private readonly DmaController _primary;
    private readonly DmaController _secondary;

    /// <summary>
    ///     Initializes the DMA system with both controllers and registers the ISA port map.
    /// </summary>
    /// <param name="memory">The emulator memory interface backing DMA transfers.</param>
    /// <param name="state">CPU state used for DMA callbacks in the base handler.</param>
    /// <param name="ioPortDispatcher">Dispatcher used to register handled IO ports.</param>
    /// <param name="failOnUnhandledPort">Whether an exception is thrown if an IO port is unknown.</param>
    /// <param name="loggerService">Logging infrastructure for DMA diagnostics.</param>
    /// <param name="wrappingMask">
    ///     Set to 0xFFFFFFFF to emulate EMM386 behavior that ignores segment boundaries.
    ///     See <see href="https://www.os2museum.com/wp/8237a-dma-page-fun/" /> for context.
    /// </param>
    public DmaBus(
        IMemory memory,
        State state,
        IOPortDispatcher ioPortDispatcher,
        bool failOnUnhandledPort,
        ILoggerService loggerService, uint wrappingMask = 0xFFFF) : base(state, failOnUnhandledPort, loggerService) {
        _logger = loggerService;
        _primary = new DmaController(0, memory, loggerService, wrappingMask);
        _secondary = new DmaController(1, memory, loggerService, wrappingMask);

        for (byte i = 0; i < _channels.Length; i++) {
            DmaController controller = i < 4 ? _primary : _secondary;
            byte channelIndex = (byte)(i % 4);
            DmaChannel? channel = controller.GetChannel(channelIndex);
            _channels[i] = channel ?? throw new InvalidOperationException($"DMA channel {i} initialization failed.");
        }

        InitializePortHandlers(ioPortDispatcher);
    }

    /// <summary>
    ///     Retrieves the DMA channel for the given absolute channel number (0..7).
    /// </summary>
    public DmaChannel? GetChannel(byte channelNumber) {
        return channelNumber < _channels.Length ? _channels[channelNumber] : null;
    }

    /// <summary>
    ///     Resets the specified DMA channel, clearing any pending transfers.
    /// </summary>
    public void ResetChannel(byte channelNumber) {
        switch (channelNumber) {
            case < 4:
                _primary.ResetChannel(channelNumber);
                break;
            case < 8:
                _secondary.ResetChannel((byte)(channelNumber - 4));
                break;
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (TryDecodeControllerPort(port, out DmaController controller, out byte register)) {
            return controller.ReadRegister(register);
        }

        if (PageRegisterToChannel.TryGetValue(port, out byte channelIndex)) {
            return _channels[channelIndex].PageRegisterValue;
        }

        _logger.Warning("DMA: Read from undefined port 0x{Port:X4}", port);
        return 0xFF;
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        byte low = ReadByte(port);
        byte high = ReadByte(port);
        return (ushort)(low | (high << 8));
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (TryDecodeControllerPort(port, out DmaController controller, out byte register)) {
            controller.WriteRegister(register, value);
            return;
        }

        if (PageRegisterToChannel.TryGetValue(port, out byte channelIndex)) {
            _channels[channelIndex].SetPage(value);
            return;
        }

        _logger.Warning("DMA: Write to undefined port 0x{Port:X4} value 0x{Value:X2}", port, value);
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte(port, (byte)((value >> 8) & 0xFF));
    }

    private void InitializePortHandlers(IOPortDispatcher dispatcher) {
        // Primary controller ports 0x00-0x0F.
        for (ushort port = 0x00; port <= 0x0F; port++) {
            dispatcher.AddIOPortHandler(port, this);
        }

        // Secondary controller ports 0xC0-0xDE (even addresses).
        for (ushort offset = 0; offset < 0x10; offset++) {
            ushort port = (ushort)(0xC0 + (offset << 1));
            dispatcher.AddIOPortHandler(port, this);
        }

        // Page registers.
        foreach (ushort port in PageRegisterToChannel.Keys) {
            dispatcher.AddIOPortHandler(port, this);
        }
    }

    private bool TryDecodeControllerPort(ushort port, out DmaController controller, out byte register) {
        switch (port) {
            case <= 0x0F:
                controller = _primary;
                register = (byte)port;
                return true;
            case >= 0xC0 and <= 0xDE when (port & 0x1) == 0:
                controller = _secondary;
                register = (byte)((port - 0xC0) >> 1);
                return true;
        }

        controller = _primary;
        register = 0;
        return false;
    }
}