namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>
/// Request packet offsets used by MSCDEX device-driver commands.
/// </summary>
internal static class MscdexRequestOffsets {
    internal const uint RequestSubunitOffset = 1;
    internal const uint RequestCommandOffset = 2;
    internal const uint RequestStatusOffset = 3;
    internal const uint RequestAddressingModeOffset = 13;
    internal const uint IoctlBufferPtrOffset = 14;
    internal const uint PlayAudioStartLbaOffset = 14;
    internal const uint PlayAudioSectorCountOffset = 18;
    internal const uint ReadLongSectorCountOffset = 18;
    internal const uint ReadLongStartSectorOffset = 20;
    internal const uint ReadLongRawFlagOffset = 24;
}