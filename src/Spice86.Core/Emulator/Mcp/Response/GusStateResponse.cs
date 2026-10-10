namespace Spice86.Core.Emulator.Mcp.Response;

using System.Collections.Generic;

internal sealed record GusStateResponse
{
    public required int BasePort { get; init; }

    public required byte PlaybackIrq { get; init; }

    public required byte RecordingIrq { get; init; }

    public required byte PlaybackDma { get; init; }

    public required byte RecordingDma { get; init; }

    public required bool IsRunning { get; init; }

    public required bool IsDacEnabled { get; init; }

    public required bool AreIrqsEnabled { get; init; }

    public required bool AreLatchesEnabled { get; init; }

    public required int ActiveVoices { get; init; }

    public required uint ActiveVoiceMask { get; init; }

    public required int SampleRate { get; init; }

    public required byte ResetRegister { get; init; }

    public required byte MixControlRegister { get; init; }

    public required byte TimerControlRegister { get; init; }

    public required byte TimerStatusRegister { get; init; }

    public required byte SampleControlRegister { get; init; }

    public required byte DmaControlRegister { get; init; }

    public required byte IrqStatusRegister { get; init; }

    public required byte AdlibCommandRegister { get; init; }

    public required byte SelectedRegister { get; init; }

    public required ushort SelectedRegisterData { get; init; }

    public required byte SelectedVoiceIndex { get; init; }

    public required int DramAddress { get; init; }

    public required int DramSizeBytes { get; init; }

    public required string UltraSndEnvironmentVariable { get; init; }

    public required string UltraDirEnvironmentVariable { get; init; }

    public required string MixerChannelName { get; init; }

    public required int MixerChannelSampleRate { get; init; }

    public required bool MixerChannelEnabled { get; init; }

    public required IReadOnlyList<GusTimerStateResponse> Timers { get; init; }
}
