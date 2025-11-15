namespace Spice86.Core.Emulator.IOPorts;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices;
using Spice86.Shared.Interfaces;

/// <summary>
///     Coordinates delegate-based registration for emulated I/O port reads and writes on top of the shared
///     <see cref="IOPortDispatcher" />.
/// </summary>
public sealed class IOPortHandlerRegistry(
    IOPortDispatcher dispatcher, State state, ILoggerService loggerService, bool failOnUnhandledPort) {
    private readonly Dictionary<ushort, DelegatePortHandler> _handlers = new();

    /// <summary>
    ///     Disposes all registered handlers and removes their port mappings from the dispatcher.
    /// </summary>
    public void Reset() {
        int handlerCount = _handlers.Count;
        if (handlerCount == 0) {
            loggerService.Debug("IoPortHandlerRegistry reset requested but no handlers are currently registered.");
            return;
        }

        loggerService.Debug("Resetting IoPortHandlerRegistry; removing {HandlerCount} port handler(s).", handlerCount);

        foreach ((ushort port, DelegatePortHandler handler) in _handlers) {
            handler.Dispose();
            dispatcher.RemoveIOPortHandler(port);
        }

        _handlers.Clear();
    }

    /// <summary>
    ///     Registers a read delegate for a contiguous range of I/O ports.
    /// </summary>
    /// <param name="handler">Delegate that performs the read.</param>
    /// <param name="port">First port covered by the delegate.</param>
    /// <param name="portRange">Number of consecutive ports serviced by the delegate.</param>
    public void RegisterReadHandler(IoReadDelegate handler, ushort port, ushort portRange = 1) {
        if (!ValidateRange(port, ref portRange)) {
            return;
        }

        loggerService.Debug("Registering read handler for port 0x{Port:X4} across {PortRange} port(s).", port,
            portRange);

        for (ushort offset = 0; offset < portRange; offset++) {
            ushort actualPort = (ushort)(port + offset);
            DelegatePortHandler entry = GetOrCreateHandler(actualPort);
            entry.ReadHandler = handler;
        }
    }

    /// <summary>
    ///     Registers a write delegate for a contiguous range of I/O ports.
    /// </summary>
    /// <param name="handler">Delegate that performs the write.</param>
    /// <param name="port">First port covered by the delegate.</param>
    /// <param name="portRange">Number of consecutive ports serviced by the delegate.</param>
    public void RegisterWriteHandler(IoWriteDelegate handler, ushort port, ushort portRange = 1) {
        if (!ValidateRange(port, ref portRange)) {
            return;
        }

        loggerService.Debug("Registering write handler for port 0x{Port:X4} across {PortRange} port(s).", port,
            portRange);

        for (ushort offset = 0; offset < portRange; offset++) {
            ushort actualPort = (ushort)(port + offset);
            DelegatePortHandler entry = GetOrCreateHandler(actualPort);
            entry.WriteHandler = handler;
        }
    }

    /// <summary>
    ///     Removes any registered read and write delegates for the specified port range.
    /// </summary>
    /// <param name="port">First port covered by the range.</param>
    /// <param name="portRange">Number of consecutive ports to release.</param>
    public void FreeHandler(ushort port, ushort portRange = 1) {
        if (!ValidateRange(port, ref portRange)) {
            return;
        }

        for (ushort offset = 0; offset < portRange; offset++) {
            ushort actualPort = (ushort)(port + offset);
            if (!_handlers.Remove(actualPort, out DelegatePortHandler? handler)) {
                loggerService.Warning("Requested to free handler on port 0x{Port:X4}, but no handler is registered.",
                    actualPort);
                continue;
            }

            handler.Dispose();
            dispatcher.RemoveIOPortHandler(actualPort);

            loggerService.Debug("Freed handler previously registered on port 0x{Port:X4}.", actualPort);
        }
    }

    /// <summary>
    ///     Reads a value from the specified port using the registered byte delegate.
    /// </summary>
    /// <param name="port">Port number to read from.</param>
    /// <returns>The value returned by the handler as an unsigned integer.</returns>
    public uint Read(ushort port) {
        return ReadByteInternal(port);
    }

    /// <summary>
    ///     Writes a value to the specified port using the registered byte delegate.
    /// </summary>
    /// <param name="port">Port number to write to.</param>
    /// <param name="value">Value to write. The handler uses only the low byte.</param>
    public void Write(ushort port, uint value) {
        WriteByteInternal(port, NumericConverters.CheckCast<byte, uint>(value));
    }

    /// <summary>
    ///     Verifies that the requested range is non-zero and within the remaining 16-bit port space.
    /// </summary>
    /// <param name="port">Starting port for the range.</param>
    /// <param name="portRange">Requested number of ports; may be updated if it exceeds the available space.</param>
    /// <returns><see langword="true" /> when the range is usable; otherwise <see langword="false" />.</returns>
    private bool ValidateRange(ushort port, ref ushort portRange) {
        if (portRange == 0) {
            loggerService.Error("Ignoring request that specifies a zero-length port range for port 0x{Port:X4}.", port);
            return false;
        }

        int maxRange = ushort.MaxValue - port + 1;
        if (portRange > maxRange) {
            loggerService.Error(
                "Requested port range {RequestedRange} for port 0x{Port:X4} exceeds available ports. Capping to {CappedRange}.",
                portRange, port, maxRange);
            portRange = (ushort)maxRange;
        }

        return true;
    }

    private byte ReadByteInternal(ushort port) {
        return _handlers.TryGetValue(port, out DelegatePortHandler? handler) ? handler.ReadByte(port) : (byte)0;
    }

    private void WriteByteInternal(ushort port, byte value) {
        if (_handlers.TryGetValue(port, out DelegatePortHandler? handler)) {
            handler.WriteByte(port, value);
        }
    }

    private DelegatePortHandler GetOrCreateHandler(ushort port) {
        if (_handlers.TryGetValue(port, out DelegatePortHandler? existing)) {
            return existing;
        }

        var handler = new DelegatePortHandler(this, state, failOnUnhandledPort, loggerService);
        _handlers[port] = handler;
        dispatcher.AddIOPortHandler(port, handler);

        loggerService.Debug("Created delegate handler structure for port 0x{Port:X4}.", port);

        return handler;
    }

    private sealed class DelegatePortHandler(
        IOPortHandlerRegistry owner, State state, bool failOnUnhandledPort, ILoggerService loggerService)
        : DefaultIOPortHandler(state, failOnUnhandledPort, loggerService), IDisposable {
        /// <summary>
        ///     Gets or sets the delegate that services byte reads for the port.
        /// </summary>
        public IoReadDelegate? ReadHandler { get; set; }

        /// <summary>
        ///     Gets or sets the delegate that services byte writes for the port.
        /// </summary>
        public IoWriteDelegate? WriteHandler { get; set; }

        /// <inheritdoc />
        public void Dispose() {
            ReadHandler = null;
            WriteHandler = null;
        }

        /// <inheritdoc />
        public override byte ReadByte(ushort port) {
            if (ReadHandler == null) {
                return base.ReadByte(port);
            }

            uint result = ReadHandler(port);
            return (byte)(result & 0xFF);
        }

        /// <inheritdoc />
        public override ushort ReadWord(ushort port) {
            byte low = owner.ReadByteInternal(port);
            byte high = owner.ReadByteInternal((ushort)(port + 1));
            return (ushort)(low | (high << 8));
        }

        /// <inheritdoc />
        public override uint ReadDWord(ushort port) {
            ushort low = ReadWord(port);
            ushort high = ReadWord((ushort)(port + 2));
            return low | ((uint)high << 16);
        }

        /// <inheritdoc />
        public override void WriteByte(ushort port, byte value) {
            if (WriteHandler != null) {
                WriteHandler(port, value);
                return;
            }

            base.WriteByte(port, value);
        }

        /// <inheritdoc />
        public override void WriteWord(ushort port, ushort value) {
            owner.WriteByteInternal(port, (byte)(value & 0xFF));
            owner.WriteByteInternal((ushort)(port + 1), (byte)(value >> 8));
        }

        /// <inheritdoc />
        public override void WriteDWord(ushort port, uint value) {
            WriteWord(port, (ushort)(value & 0xFFFF));
            WriteWord((ushort)(port + 2), (ushort)(value >> 16));
        }
    }
}