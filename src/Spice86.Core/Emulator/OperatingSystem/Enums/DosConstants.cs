namespace Spice86.Core.Emulator.OperatingSystem.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal static class DosConstants {
    public const ushort DosSdaSegment = 0xb2;
    public const ushort DosSdaOffset = 0x0;
    public const ushort DosCurrentDirectoryStructureSegment = 0x108;
    public const ushort DosInfoBlockSegment = 0x80;
    public const ushort DosConDrvSegment = 0Xa0;
    public const ushort DosConStringSegment = 0xa8;
    public const ushort DosFirstShell = 0x118;
    public const ushort DosFirstUsableMemorySegment = 0x16f;
    public const ushort DosPrivateSegmentStart = 0xc800;
    public const ushort DosPrivateSegmentEnd = 0xd000;
    public const int SftHeaderSize = 6;
    public const int SftEntrySize = 59;
    public const uint SftEndPointer = 0xffffffff;
    public const ushort SftNextTableOffset = 0x0;
    public const ushort SftNumberOfFilesOffset = 0x04;
    public const int FakeSftEntries = 16;
}
