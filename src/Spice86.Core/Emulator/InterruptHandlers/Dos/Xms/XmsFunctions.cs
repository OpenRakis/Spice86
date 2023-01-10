namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

internal static class XmsFunctions {
    public const byte GetVersionNumber = 0x00;
    public const byte RequestHighMemoryArea = 0x01;
    public const byte ReleaseHighMemoryArea = 0x02;
    public const byte GlobalEnableA20 = 0x03;
    public const byte GlobalDisableA20 = 0x04;
    public const byte LocalEnableA20 = 0x05;
    public const byte LocalDisableA20 = 0x06;
    public const byte QueryA20 = 0x07;
    public const byte QueryFreeExtendedMemory = 0x08;
    public const byte AllocateExtendedMemoryBlock = 0x09;
    public const byte FreeExtendedMemoryBlock = 0x0A;
    public const byte MoveExtendedMemoryBlock = 0x0B;
    public const byte LockExtendedMemoryBlock = 0x0C;
    public const byte UnlockExtendedMemoryBlock = 0x0D;
    public const byte GetHandleInformation = 0x0E;
    public const byte ReallocateExtendedMemoryBlock = 0x0F;
    public const byte RequestUpperMemoryBlock = 0x10;
    public const byte ReleaseUpperMemoryBlock = 0x11;
    public const byte ReallocateUpperMemoryBlock = 0x12;
    public const byte QueryAnyFreeExtendedMemory = 0x88;
    public const byte AllocateAnyExtendedMemory = 0x89;
}
