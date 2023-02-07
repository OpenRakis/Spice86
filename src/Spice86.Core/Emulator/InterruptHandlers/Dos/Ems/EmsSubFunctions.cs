namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

internal static class EmsSubFunctions {
    public const byte MapUnmapPages = 0x00;
    public const byte HandleNameGet = 0x00;
    public const byte HandleNameSet = 0x01;
    public const byte GetHardwareInformationUnallocatedRawPages = 0x01;
    public const byte MoveExchangeMove = 0x00;
}
