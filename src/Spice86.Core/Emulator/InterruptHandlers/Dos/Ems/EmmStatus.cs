namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public static class EmmStatus {
    public const byte EmmNoError = 0x00;
    public const byte EmmSoftMal = 0x80;
    public const byte EmmHardMal = 0x81;
    public const byte EmmInvalidHandle = 0x83;
    public const byte EmmFuncNoSup = 0x84;
    public const byte EmmOutOfHandles = 0x85;
    public const byte EmmSaveMapError = 0x86;
    public const byte EmmOutOfPhysicalPages = 0x87;
    public const byte EmmOutOfLogicalPages = 0x88;
    public const byte EmmZeroPages = 0x89;
    public const byte EmsLogicalPageOutOfRange = 0x8a;
    public const byte EmsIllegalPhysicalPage = 0x8b;
    public const byte EmmPageMapSaved = 0x8d;
    public const byte EmmNoSavedPageMap = 0x8e;
    public const byte EmmInvalidSubFunction = 0x8f;
    public const byte EmmFeatNoSup = 0x91;
    public const byte EmmMoveOverlap = 0x92;
    public const byte EmmMoveOverlapi = 0x97;
    public const byte EmmNotFound = 0xa0;
}