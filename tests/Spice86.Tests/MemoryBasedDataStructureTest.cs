namespace Spice86.Tests;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

public class MemoryBasedDataStructureTest {
    private const byte ExpectedUInt8 = 0x01;
    private const ushort ExpectedUInt16 = 0x0102;
    private const uint ExpectedUInt32 = 0x01020304;
    private const string ExpectedString = "0123456789";
    private static readonly int ExpectedStringLength = ExpectedString.Length + 1;
    private static readonly SegmentedAddress ExpectedSegmentedAddress = new (0x0708, 0x0900);

    private static readonly byte[] ExpectedUInt8Array = { 0x01, 0x02 };
    private static readonly ushort[] ExpectedUInt16Array = { 0x0101, 0x0202 };
    private static readonly uint[] ExpectedUInt32Array = { 0x01010101, 0x02020202 };
    private static readonly SegmentedAddress[] ExpectedSegmentedAddressArray = { new (0x01, 0x02), new (0x03, 0x04) };

    // Offset in struct for read test
    private const uint ReadOffset = 10;
    // Offset in struct for write test
    private const uint WriteOffset = 30;
    // Address of struct in memory
    private const uint StructAddress = 10;
    // Physical address in memory for read test
    private const uint ReadAddress = StructAddress + ReadOffset;
    // Physical address in memory for write test
    private const uint WriteAddress = StructAddress + WriteOffset;

    [Fact]
    public void CanMapUInt8() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        data.UInt8[ReadAddress] = ExpectedUInt8;

        // Act & Assert
        // Read
        Assert.Equal(ExpectedUInt8, memoryBasedDataStructure.UInt8[ReadOffset]);
        // Write
        memoryBasedDataStructure.UInt8[WriteOffset] = ExpectedUInt8;
        Assert.Equal(ExpectedUInt8, data.UInt8[WriteAddress]);
    }

    [Fact]
    public void CanMapUInt16() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        data.UInt16[ReadAddress] = ExpectedUInt16;

        // Act & Assert
        // Read
        Assert.Equal(ExpectedUInt16, memoryBasedDataStructure.UInt16[ReadOffset]);
        // Write
        memoryBasedDataStructure.UInt16[WriteOffset] = ExpectedUInt16;
        Assert.Equal(ExpectedUInt16, data.UInt16[WriteAddress]);
    }

    [Fact]
    public void CanMapUInt32() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        data.UInt32[ReadAddress] = ExpectedUInt32;
        memoryBasedDataStructure.UInt32[WriteOffset] = ExpectedUInt32;

        // Act & Assert
        // Read
        Assert.Equal(ExpectedUInt32, memoryBasedDataStructure.UInt32[ReadOffset]);
        // Write
        Assert.Equal(ExpectedUInt32, data.UInt32[WriteAddress]);
    }

    [Fact]
    public void CanMapSegmentedAddress() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        data.SegmentedAddress[ReadAddress] = ExpectedSegmentedAddress;

        // Act & Assert
        // Read
        Assert.Equal(ExpectedSegmentedAddress, memoryBasedDataStructure.SegmentedAddress[ReadOffset]);
        // Write
        memoryBasedDataStructure.SegmentedAddress[WriteOffset] = ExpectedSegmentedAddress;
        Assert.Equal(ExpectedSegmentedAddress, data.SegmentedAddress[WriteAddress]);
    }

    [Fact]
    public void CanMapString() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        data.SetZeroTerminatedString(ReadAddress, ExpectedString, ExpectedStringLength);

        // Act & Assert
        // Read
        Assert.Equal(ExpectedString, memoryBasedDataStructure.GetZeroTerminatedString(ReadOffset, ExpectedStringLength));
        // Write
        memoryBasedDataStructure.SetZeroTerminatedString(WriteOffset, ExpectedString, ExpectedStringLength);
        Assert.Equal(ExpectedString, data.GetZeroTerminatedString(WriteAddress, ExpectedStringLength));
    }

    [Fact]
    public void CanMapUInt8Array() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        CopyArrayToIndexer(ExpectedUInt8Array, data.UInt8, ReadAddress, 1);

        // Act & Assert
        UInt8Array uInt8Array = memoryBasedDataStructure.GetUInt8Array(ReadOffset, ExpectedUInt8Array.Length);
        // Read
        AssertArrayEqualsCollection(ExpectedUInt8Array, uInt8Array);

        // Write
        WriteIndex0AndAssertIndex1Untouched<byte>(ExpectedUInt8Array, uInt8Array, data.UInt8, 1, 0);
    }

    [Fact]
    public void CanMapUInt16Array() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        CopyArrayToIndexer(ExpectedUInt16Array, data.UInt16, ReadAddress, 2);

        // Act & Assert
        UInt16Array uInt16Array = memoryBasedDataStructure.GetUInt16Array(ReadOffset, ExpectedUInt16Array.Length);
        // Read
        AssertArrayEqualsCollection(ExpectedUInt16Array, uInt16Array);

        // Write
        WriteIndex0AndAssertIndex1Untouched<ushort>(ExpectedUInt16Array, uInt16Array, data.UInt16, 2, 0);
    }

    [Fact]
    public void CanMapUInt32Array() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        CopyArrayToIndexer(ExpectedUInt32Array, data.UInt32, ReadAddress, 4);

        // Act & Assert
        UInt32Array uInt32Array = memoryBasedDataStructure.GetUInt32Array(ReadOffset, ExpectedUInt32Array.Length);
        // Read
        AssertArrayEqualsCollection(ExpectedUInt32Array, uInt32Array);
        // Write
        WriteIndex0AndAssertIndex1Untouched<uint>(ExpectedUInt32Array, uInt32Array, data.UInt32, 4, 0);
    }

    [Fact]
    public void CanMapSegmentedAddressArray() {
        // Arrange
        (ByteArrayBasedIndexable data, MemoryBasedDataStructure memoryBasedDataStructure) = Init(StructAddress);
        CopyArrayToIndexer(ExpectedSegmentedAddressArray, data.SegmentedAddress, ReadAddress, 4);

        // Act & Assert
        SegmentedAddressArray segmentedAddressArray = memoryBasedDataStructure.GetSegmentedAddressArray(ReadOffset, ExpectedUInt32Array.Length);
        // Read
        AssertArrayEqualsCollection(ExpectedSegmentedAddressArray, segmentedAddressArray);
        // Write
        WriteIndex0AndAssertIndex1Untouched<SegmentedAddress>(ExpectedSegmentedAddressArray, segmentedAddressArray, data.SegmentedAddress, 4, new SegmentedAddress(0, 0));
    }

    private void CopyArrayToIndexer<T>(IReadOnlyList<T> array, Indexer<T> data, uint baseAddress, int elementSize) {
        for (int i = 0; i < array.Count; i++) {
            data[(uint)(baseAddress + i * elementSize)] = array[i];
        }
    }

    private void AssertArrayEqualsCollection<T>(IReadOnlyList<T> expectedArray, ICollection<T> collection) {
        Assert.Equal(expectedArray.Count, collection.Count);

        int index = 0;
        foreach (T actual in collection) {
            T expected = expectedArray[index];
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
                Assert.Fail($"Values at index {index} do not match. Expected {expected} but got {actual}");
            }

            index++;
        }
    }

    private void WriteIndex0AndAssertIndex1Untouched<T>(IReadOnlyList<T> expectedArray, IList<T> inMemoryArray, Indexer<T> memoryData, int elementSize, T writeValue) {
        inMemoryArray[0] = writeValue;
        Assert.Equal(writeValue, memoryData[ReadAddress]);
        Assert.Equal(expectedArray[1], memoryData[(uint)(ReadAddress + elementSize)]);
    }

    private (ByteArrayBasedIndexable Data, MemoryBasedDataStructure MemoryBasedDataStructure) Init(uint offset) {
        ByteArrayBasedIndexable data = new ByteArrayBasedIndexable(new byte[100]);
        MemoryBasedDataStructure memoryBasedDataStructure = new MemoryBasedDataStructure(data.ReaderWriter, offset);
        return (data, memoryBasedDataStructure);
    }
}