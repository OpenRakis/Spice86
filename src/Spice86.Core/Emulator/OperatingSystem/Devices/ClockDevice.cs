namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents the clock device for MS-DOS.
/// </summary>
public class ClockDevice : CharacterDevice {
    private readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClockDevice"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public ClockDevice(ILoggerService loggerService, DeviceAttributes attributes,
        IMemory memory, ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, attributes, "CLOCK$", strategy, interrupt) {
        _memory = memory;
    }

    public override string Name => "CLOCK$";

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => 0;

    public override long Position { get; set; }

    /// <summary>
    /// Reads the current time from the clock device.
    /// </summary>
    /// <param name="buffer">The buffer to store the time data.</param>
    /// <param name="offset">The offset in the buffer to start writing.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count) {
        if (buffer == null) {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || offset >= buffer.Length) {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count <= 0 || offset + count > buffer.Length) {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        // Get the current time from the timer.
        DateTime now = DateTime.Now;

        // Format the time as HH:MM:SS (DOS-style).
        string timeString = now.ToString("HH:mm:ss");

        // Convert the time string to bytes and copy it to the buffer.
        byte[] timeBytes = System.Text.Encoding.ASCII.GetBytes(timeString);
        int bytesToCopy = Math.Min(count, timeBytes.Length);
        Array.Copy(timeBytes, 0, buffer, offset, bytesToCopy);

        return bytesToCopy;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Unsupported Writing to {@Device}", this);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Unsupported Seeking {@Device}", this);
        }
        return 0;
    }

    public override void SetLength(long value) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Unsupported Setting length {@Device}", this);
        }
    }

    public override void Flush() {
        // No-op for the clock device.
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Unsupported Flushing {@Device}", this);
        }
    }

    /// <summary>
    /// Handles IOCTL operations for the clock device.
    /// </summary>
    /// <param name="address">The memory address for the operation.</param>
    /// <param name="size">The size of the operation.</param>
    /// <param name="returnCode">The return code for the operation.</param>
    /// <returns>True if the operation was successful; otherwise, false.</returns>
    public override bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {

        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            Logger.Verbose("Reading from {@Device} via DOS IOControl", this);
        }

        DateTime now = DateTime.Now;
        ushort year = (ushort)now.Year;
        byte month = (byte)now.Month;
        byte day = (byte)now.Day;

        // Pack the date into memory at the specified address.
        byte[] dateBytes =
        [
            (byte)(year & 0xFF), // Low byte of year
            (byte)(year >> 8),   // High byte of year
            month,
            day,
        ];

        // Write the date to the specified memory address.
        _memory.LoadData(address, dateBytes, dateBytes.Length);

        returnCode = 0; // Success
        return true;
    }

    public override bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Unsupported Writing to {@Device} via DOS IOControl", this);
        }

        // Writing to the clock device is not supported.
        returnCode = null;
        return false;
    }
}
