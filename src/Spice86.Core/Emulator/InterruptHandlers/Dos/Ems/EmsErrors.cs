namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public static class EmsErrors {
    public const byte EmsInvalidSubFunction = 0x8F;
    public const byte EmmNoError = 0x00;
    public const byte EmmSoftMal = 0x80;
    public const byte EmmHardMal = 0x81;
    public const byte EmmInvalidHandle = 0x83;
    public const byte EmmFuncNoSup = 0x84;
    public const byte EmmOutOfHandles = 0x85;
    public const byte EmmSaveMapError = 0x86;
    public const byte EmmOutOfPhys = 0x87;
    public const byte EmmOutOfLog = 0x88;
    public const byte EmmZeroPages = 0x89;
    public const byte EmmLogOutRange = 0x8a;
    public const byte EmmIllPhys = 0x8b;
    public const byte EmmPageMapSaved = 0x8d;
    public const byte EmmNoSavedPageMap = 0x8e;
    public const byte EmmInvalidSub = 0x8f;
    public const byte EmmFeatNoSup = 0x91;
    public const byte EmmMoveOverlap = 0x92;
    public const byte EmmMoveOverlapi = 0x97;
    public const byte EmmNotFound = 0xa0;
}