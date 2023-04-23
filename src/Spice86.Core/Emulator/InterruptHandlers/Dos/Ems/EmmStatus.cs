namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public static class EmmStatus {
    public const byte EmmNoError = 0x00;
    public const byte EmmInvalidHandle = 0x83;
    public const byte EmmFuncNoSup = 0x84;
    public const byte EmmOutOfHandles = 0x85;
    public const byte EmmSaveMapError = 0x86;
    public const byte NotEnoughEmmPages = 0x87;
    public const byte TriedToAllocateZeroPages = 0x89;
    public const byte EmsLogicalPageOutOfRange = 0x8a;
    public const byte EmsIllegalPhysicalPage = 0x8b;
    public const byte EmmPageMapSaved = 0x8d;
    public const byte EmmInvalidSubFunction = 0x8f;
}