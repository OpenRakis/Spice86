namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.Devices.Sound;

using System.Collections.Generic;

internal sealed record GusIrqStateResponse
{
    public required byte PlaybackIrq { get; init; }

    public required byte RecordingIrq { get; init; }

    public required byte IrqStatusRegister { get; init; }

    public required GusIrqStatus IrqStatus { get; init; }

    public required bool AreIrqsEnabled { get; init; }

    public required bool AreLatchesEnabled { get; init; }

    public required uint VoiceWaveIrqMask { get; init; }

    public required uint VoiceVolumeIrqMask { get; init; }

    public required byte NextVoiceToReport { get; init; }

    public required IReadOnlyList<GusTimerStateResponse> Timers { get; init; }
}
