namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.Devices.Sound;

internal sealed record GusDmaStateResponse
{
    public required byte PlaybackDma { get; init; }

    public required byte RecordingDma { get; init; }

    public required byte DmaControlRegister { get; init; }

    public required GusDmaControl DmaControl { get; init; }

    public required byte SampleControlRegister { get; init; }

    public required bool IsTransfer16Bit { get; init; }

    public required bool AreSamples16Bit { get; init; }

    public required ushort DmaAddressRegister { get; init; }

    public required byte DmaAddressNibble { get; init; }

    public required uint DramOffset { get; init; }

    public required bool TerminalCountIrqPending { get; init; }
}
