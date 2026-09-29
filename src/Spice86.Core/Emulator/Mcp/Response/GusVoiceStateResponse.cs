namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.Devices.Sound;

internal sealed record GusVoiceStateResponse
{
    public required int Index { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsPlaying { get; init; }

    public required bool Is16BitSample { get; init; }

    public required int WaveStart { get; init; }

    public required int WaveEnd { get; init; }

    public required int WavePos { get; init; }

    public required int WaveInc { get; init; }

    public required ushort WaveRate { get; init; }

    public required byte WaveStateRegister { get; init; }

    public required GusVoiceControl WaveControl { get; init; }

    public required int VolStart { get; init; }

    public required int VolEnd { get; init; }

    public required int VolPos { get; init; }

    public required int VolInc { get; init; }

    public required ushort VolRate { get; init; }

    public required byte VolStateRegister { get; init; }

    public required GusVoiceControl VolControl { get; init; }

    public required byte PanPosition { get; init; }

    public required bool WaveIrqPending { get; init; }

    public required bool VolumeIrqPending { get; init; }

    public required uint Generated8BitMs { get; init; }

    public required uint Generated16BitMs { get; init; }
}
