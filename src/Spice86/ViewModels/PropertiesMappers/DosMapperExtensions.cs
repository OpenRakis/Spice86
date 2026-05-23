namespace Spice86.ViewModels.PropertiesMappers;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Mapping helpers for DOS Info POCOs. Pure read-only snapshots.
/// </summary>
internal static class DosMapperExtensions {
    private const int EnvironmentPreviewMaxBytes = 512;
    private const int EnvironmentScanMaxBytes = 4096;
    private const int EnvironmentProgramPathMaxBytes = 260;

    public static void CopyToDosMemorySummaryInfo(this DosMemoryManager memoryManager,
        DosMemorySummaryInfo info,
        ExpandedMemoryManager? ems,
        ExtendedMemoryManager? xms) {
        long freeBytes = 0;
        long usedBytes = 0;
        long largestFreeBytes = 0;
        int mcbCount = 0;
        int freeMcbCount = 0;
        foreach (DosMemoryControlBlock block in memoryManager.EnumerateBlocks()) {
            if (!block.IsValid) {
                break;
            }
            mcbCount++;
            long sizeBytes = block.AllocationSizeInBytes;
            if (block.IsFree) {
                freeMcbCount++;
                freeBytes += sizeBytes;
                if (sizeBytes > largestFreeBytes) {
                    largestFreeBytes = sizeBytes;
                }
            } else {
                usedBytes += sizeBytes;
            }
        }
        info.ConventionalFreeBytes = freeBytes;
        info.ConventionalUsedBytes = usedBytes;
        info.LargestFreeBlockBytes = largestFreeBytes;
        info.McbCount = mcbCount;
        info.FreeMcbCount = freeMcbCount;

        info.EmsEnabled = ems is not null;
        if (ems is not null) {
            info.EmsTotalPages = EmmMemory.TotalPages;
            info.EmsFreePages = ems.GetFreePageCount();
            info.EmsAllocatedHandles = ems.EmmHandles.Count;
        } else {
            info.EmsTotalPages = 0;
            info.EmsFreePages = 0;
            info.EmsAllocatedHandles = 0;
        }

        info.XmsEnabled = xms is not null;
        if (xms is not null) {
            info.XmsTotalBytes = (long)ExtendedMemoryManager.XmsMemorySize * 1024L;
            info.XmsFreeBytes = xms.TotalFreeMemory;
            info.XmsAllocatedHandles = xms.HandlesSnapshot.Count;
        } else {
            info.XmsTotalBytes = 0;
            info.XmsFreeBytes = 0;
            info.XmsAllocatedHandles = 0;
        }
    }

    public static void CopyToDosPspInfo(this DosProgramSegmentPrefix psp, DosPspInfo info,
        ushort pspSegment, ushort currentPspSegment, string ownerName) {
        info.Segment = FormatSegment(pspSegment);
        info.IsCurrent = pspSegment == currentPspSegment;
        info.ParentSegment = FormatSegment(psp.ParentProgramSegmentPrefix);
        info.PreviousPspAddress = psp.PreviousPspAddress.ToString();
        string resolvedOwnerName = ownerName;
        if (string.IsNullOrWhiteSpace(resolvedOwnerName)) {
            resolvedOwnerName = ReadEnvironmentProgramName(psp);
        }
        info.OwnerName = resolvedOwnerName;
        info.CurrentSizeParagraphs = psp.CurrentSize;
        info.EnvironmentSegment = FormatSegment(psp.EnvironmentTableSegment);
        info.StackPointer = psp.StackPointer.ToString();
        info.MaxOpenFiles = psp.MaximumOpenFiles;
        info.FileTableAddress = psp.FileTableAddress.ToString();
        info.TerminateAddress = psp.TerminateAddress.ToString();
        info.BreakAddress = psp.BreakAddress.ToString();
        info.CriticalErrorAddress = psp.CriticalErrorAddress.ToString();
        info.DosVersionMajor = psp.DosVersionMajor;
        info.DosVersionMinor = psp.DosVersionMinor;

        DosCommandTail tail = psp.DosCommandTail;
        info.CommandTailLength = tail.Length;
        info.CommandTailText = tail.Command ?? string.Empty;

        info.EnvironmentVariablesPreview = ReadEnvironment(psp);
    }

    public static void CopyToDosMcbInfo(this DosMemoryControlBlock block, DosMcbInfo info) {
        ushort headerSegment = (ushort)(block.DataBlockSegment - 1);
        info.HeaderSegment = FormatSegment(headerSegment);
        info.DataSegment = FormatSegment(block.DataBlockSegment);
        info.TypeByte = block.TypeField;
        if (block.IsLast) {
            info.Type = "Last (Z)";
        } else if (block.IsNonLast) {
            info.Type = "Non-last (M)";
        } else {
            info.Type = "Invalid";
        }
        if (block.IsFree) {
            info.OwnerPspSegment = "(free)";
        } else {
            info.OwnerPspSegment = FormatSegment(block.PspSegment);
        }
        if (block.IsValid) {
            if (block.Owner is null) {
                info.OwnerName = string.Empty;
            } else {
                info.OwnerName = block.Owner;
            }
        } else {
            info.OwnerName = string.Empty;
        }
        info.IsFree = block.IsFree;
        info.IsLast = block.IsLast;
        info.IsValid = block.IsValid;
        info.SizeParagraphs = block.Size;
        info.SizeBytes = block.AllocationSizeInBytes;
    }

    private static string FormatSegment(ushort segment) {
        return "0x" + segment.ToString("X4");
    }

    private static string ReadEnvironment(DosProgramSegmentPrefix psp) {
        ushort envSegment = psp.EnvironmentTableSegment;
        if (envSegment == 0) {
            return string.Empty;
        }
        uint envAddress = MemoryUtils.ToPhysicalAddress(envSegment, 0);
        long memoryLength = psp.ByteReaderWriter.Length;
        if (envAddress >= memoryLength) {
            return string.Empty;
        }

        long readableBytes = memoryLength - envAddress;
        int capacity = (int)Math.Min(readableBytes, EnvironmentPreviewMaxBytes);
        if (capacity <= 0) {
            return string.Empty;
        }

        StringBuilder builder = new();
        int count = 0;
        while (count < capacity) {
            byte first = psp.ByteReaderWriter[envAddress + (uint)count];
            if (first == 0) {
                break;
            }
            int entryStart = count;
            while (count < capacity &&
                psp.ByteReaderWriter[envAddress + (uint)count] != 0) {
                count++;
            }
            int length = count - entryStart;
            if (length > 0) {
                for (int i = 0; i < length; i++) {
                    builder.Append((char)psp.ByteReaderWriter[envAddress + (uint)(entryStart + i)]);
                }
                builder.Append(';');
            }
            count++;
        }
        return builder.ToString();
    }

    private static string ReadEnvironmentProgramName(DosProgramSegmentPrefix psp) {
        ushort envSegment = psp.EnvironmentTableSegment;
        if (envSegment == 0) {
            return string.Empty;
        }

        uint envAddress = MemoryUtils.ToPhysicalAddress(envSegment, 0);
        long memoryLength = psp.ByteReaderWriter.Length;
        if (envAddress >= memoryLength) {
            return string.Empty;
        }

        long readableBytes = memoryLength - envAddress;
        int capacity = (int)Math.Min(readableBytes, EnvironmentScanMaxBytes);
        if (capacity < 4) {
            return string.Empty;
        }

        int index = 0;
        bool foundDoubleNull = false;
        while (index + 1 < capacity) {
            byte current = psp.ByteReaderWriter[envAddress + (uint)index];
            byte next = psp.ByteReaderWriter[envAddress + (uint)(index + 1)];
            if (current == 0 && next == 0) {
                index += 2;
                foundDoubleNull = true;
                break;
            }
            index++;
        }

        if (!foundDoubleNull || index + 2 > capacity) {
            return string.Empty;
        }

        // Skip the WORD that stores the number of additional strings.
        index += 2;
        if (index >= capacity) {
            return string.Empty;
        }

        StringBuilder pathBuilder = new();
        int consumed = 0;
        while (index < capacity && consumed < EnvironmentProgramPathMaxBytes) {
            byte value = psp.ByteReaderWriter[envAddress + (uint)index];
            if (value == 0) {
                break;
            }
            pathBuilder.Append((char)value);
            index++;
            consumed++;
        }

        string path = pathBuilder.ToString();
        if (string.IsNullOrEmpty(path)) {
            return string.Empty;
        }

        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName)) {
            return path.ToUpperInvariant();
        }

        return fileName.ToUpperInvariant();
    }
}
