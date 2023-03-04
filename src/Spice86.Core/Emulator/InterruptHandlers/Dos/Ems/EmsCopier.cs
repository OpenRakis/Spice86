namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

using Spice86.Core.Emulator.Memory;
public static class EmsCopier {
    public static byte ConvToConv(Memory memory, uint sourceAddress, uint destAddress, int length) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
            return 0;

        if (sourceAddress + length > MainMemory.ConvMemorySize || destAddress + length > MainMemory.ConvMemorySize)
            return 0xA2;

        bool overlap = (sourceAddress + length - 1 >= destAddress || destAddress + length - 1 >= sourceAddress);
        bool reverse = overlap && sourceAddress > destAddress;

        if (!reverse) {
            for (uint offset = 0; offset < length; offset++)
                memory.SetUint8(destAddress + offset, memory.GetUint8(sourceAddress + offset));
        }
        else {
            for (int offset = length - 1; offset >= 0; offset--)
                memory.SetUint8(destAddress + (uint)offset, memory.GetUint8(sourceAddress + (uint)offset));
        }

        return overlap ? (byte)0x92 : (byte)0;
    }

    public static byte ConvToEms(Memory memory, uint sourceAddress, EmsHandle destHandle, int destPage, int destPageOffset, int length) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
            return 0;

        if (sourceAddress + length > MainMemory.ConvMemorySize)
            return 0xA2;
        if (destPageOffset >= ExpandedMemoryManager.PageSize)
            return 0x95;

        int offset = destPageOffset;
        uint sourceCount = sourceAddress;
        int pageIndex = destPage;
        while (length > 0) {
            int size = Math.Min(length, ExpandedMemoryManager.PageSize - offset);
            byte[]? target = destHandle.GetLogicalPage(pageIndex);
            if (target == null)
                return 0x8A;

            for (int i = 0; i < size; i++)
                target[offset + i] = memory.GetUint8(sourceCount++);

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }

    public static byte EmsToConv(EmsHandle sourceHandle, int sourcePage, int sourcePageOffset, Memory memory, uint destAddress, int length) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
            return 0;

        if (destAddress + length > MainMemory.ConvMemorySize)
            return 0xA2;
        if (sourcePageOffset >= ExpandedMemoryManager.PageSize)
            return 0x95;

        int offset = sourcePageOffset;
        uint sourceCount = destAddress;
        int pageIndex = sourcePage;
        while (length > 0)
        {
            int size = Math.Min(length, ExpandedMemoryManager.PageSize - offset);
            byte[]? source = sourceHandle.GetLogicalPage(pageIndex);
            if (source == null)
                return 0x8A;

            for (int i = 0; i < size; i++)
                memory.SetUint8(sourceCount++, source[offset + i]);

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }

    public static byte EmsToEms(EmsHandle srcHandle, int sourcePage, int sourcePageOffset, EmsHandle destHandle, int destPage, int destPageOffset, int length) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
            return 0;

        if (sourcePageOffset >= ExpandedMemoryManager.PageSize || destPageOffset >= ExpandedMemoryManager.PageSize)
            return 0x95;

        bool overlap = false;
        bool reverse = false;

        if (srcHandle == destHandle)
        {
            int sourceStart = sourcePage * ExpandedMemoryManager.PageSize + sourcePageOffset;
            int destStart = destPage * ExpandedMemoryManager.PageSize + destPageOffset;
            int sourceEnd = sourceStart + length;
            int destEnd = destStart + length;

            if (sourceStart < destStart)
            {
                overlap = sourceEnd > destStart;
            }
            else
            {
                overlap = destEnd > sourceStart;
                reverse = overlap;
            }
        }

        if (!reverse)
        {
            int sourceOffset = sourcePageOffset;
            int currentSourcePage = sourcePage;
            int destOffset = destPageOffset;
            int currentDestPage = destPage;

            while (length > 0)
            {
                int size = Math.Min(Math.Min(length, ExpandedMemoryManager.PageSize - sourceOffset), ExpandedMemoryManager.PageSize - destOffset);
                byte[]? source = srcHandle.GetLogicalPage(currentSourcePage);
                byte[]? dest = destHandle.GetLogicalPage(currentDestPage);
                if (source == null || dest == null)
                    return 0x8A;

                for (int i = 0; i < size; i++)
                    dest[destOffset + i] = source[sourceOffset + i];

                length -= size;
                sourceOffset += size;
                destOffset += size;

                if (sourceOffset == ExpandedMemoryManager.PageSize)
                {
                    sourceOffset = 0;
                    currentSourcePage++;
                }
                if (destOffset == ExpandedMemoryManager.PageSize)
                {
                    destOffset = 0;
                    currentDestPage++;
                }
            }
        }
        else
        {
            throw new NotImplementedException();
        }

        return overlap ? (byte)0x92 : (byte)0;
    }
}