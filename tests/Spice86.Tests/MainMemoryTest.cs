namespace Spice86.Tests;

using Spice86.Core.Emulator.Memory;

using System;

using Xunit;

public class MainMemoryTest {
    private readonly Memory _memory = new(new Ram(64 * 1024), is20ThAddressLineSilenced: false);

    [Fact]
    public void EnabledA20Gate_Should_ThrowExceptionAbove1MB() {
        // Arrange
        _memory.A20Gate.IsEnabled = true;

        // Act & Assert
        Assert.Throws<IndexOutOfRangeException>(() =>_memory.UInt8[0xF800, 0x8000]);
    }

    [Fact]
    public void DisabledA20Gate_Should_RolloverAddress() {
        // Arrange
        _memory.A20Gate.IsEnabled = false;

        // Act
        _memory.UInt8[0xF800, 0x8000] = 1;

        // Assert
        Assert.Equal(1, _memory.UInt8[0x00000000]);
    }

    [Fact]
    public void TestGetUint8() {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;

        // Act
        byte actual = _memory.UInt8[0x1234];

        // Assert
        Assert.Equal(0x12, actual);
    }

    [Fact]
    public void TestGetInt8() {
        // Arrange
        _memory.UInt8[0x1234] = 0xFF;

        // Act
        sbyte actual = _memory.Int8[0x1234];

        // Assert
        Assert.Equal(-1, actual);
    }

    [Fact]
    public void TestGetUint16() {
        // Arrange
        _memory.UInt8[0x1234] = 0x34;
        _memory.UInt8[0x1235] = 0x12;

        // Act
        ushort actual = _memory.UInt16[0x1234];

        // Assert
        Assert.Equal(0x1234, actual);
    }
    
    [Fact]
    public void TestGetUint16BigEndian() {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;
        _memory.UInt8[0x1235] = 0x34;

        // Act
        ushort actual = _memory.UInt16BigEndian[0x1234];

        // Assert
        Assert.Equal(0x1234, actual);
    }

    [Fact]
    public void TestGetInt16() {
        // Arrange
        _memory.UInt8[0x1234] = 0xFF;
        _memory.UInt8[0x1235] = 0xFF;

        // Act
        short actual = _memory.Int16[0x1234];

        // Assert
        Assert.Equal(-1, actual);
    }

    [Fact]
    public void TestGetUint32() {
        // Arrange
        _memory.UInt8[0x1234] = 0x34;
        _memory.UInt8[0x1235] = 0x12;
        _memory.UInt8[0x1236] = 0x78;
        _memory.UInt8[0x1237] = 0x56;

        // Act
        uint actual = _memory.UInt32[0x1234];

        // Assert
        Assert.Equal(0x56781234u, actual);
    }

    [Fact]
    public void TestGetInt32() {
        // Arrange
        _memory.UInt8[0x1234] = 0xFF;
        _memory.UInt8[0x1235] = 0xFF;
        _memory.UInt8[0x1236] = 0xFF;
        _memory.UInt8[0x1237] = 0xFF;

        // Act
        int actual = _memory.Int32[0x1234];

        // Assert
        Assert.Equal(-1, actual);
    }

    [Fact]
    public void TestSetUint8() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;

        // Act
        _memory.UInt8[0x1234] = 0x12;

        // Assert
        Assert.Equal(0x12, _memory.UInt8[0x1234]);
    }

    [Fact]
    public void TestSetInt8() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;

        // Act
        _memory.Int8[0x1234] = -1;

        // Assert
        Assert.Equal(0xFF, _memory.UInt8[0x1234]);
    }

    [Fact]
    public void TestSetUint16() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;
        _memory.UInt8[0x1235] = 0x00;

        // Act
        _memory.UInt16[0x1234] = 0x1234;

        // Assert
        Assert.Equal(0x34, _memory.UInt8[0x1234]);
        Assert.Equal(0x12, _memory.UInt8[0x1235]);
    }

    [Fact]
    public void TestSetUint16BigEndian() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;
        _memory.UInt8[0x1235] = 0x00;

        // Act
        _memory.UInt16BigEndian[0x1234] = 0x1234;

        // Assert
        Assert.Equal(0x12, _memory.UInt8[0x1234]);
        Assert.Equal(0x34, _memory.UInt8[0x1235]);
    }

    [Fact]
    public void TestSetInt16() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;
        _memory.UInt8[0x1235] = 0x00;

        // Act
        _memory.Int16[0x1234] = -1;

        // Assert
        Assert.Equal(0xFF, _memory.UInt8[0x1234]);
        Assert.Equal(0xFF, _memory.UInt8[0x1235]);
    }

    [Fact]
    public void TestSetUint32() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;
        _memory.UInt8[0x1235] = 0x00;
        _memory.UInt8[0x1236] = 0x00;
        _memory.UInt8[0x1237] = 0x00;

        // Act
        _memory.UInt32[0x1234] = 0x56781234u;

        // Assert
        Assert.Equal(0x34, _memory.UInt8[0x1234]);
        Assert.Equal(0x12, _memory.UInt8[0x1235]);
        Assert.Equal(0x78, _memory.UInt8[0x1236]);
        Assert.Equal(0x56, _memory.UInt8[0x1237]);
    }

    [Fact]
    public void TestSetInt32() {
        // Arrange
        _memory.UInt8[0x1234] = 0x00;
        _memory.UInt8[0x1235] = 0x00;
        _memory.UInt8[0x1236] = 0x00;
        _memory.UInt8[0x1237] = 0x00;

        // Act
        _memory.Int32[0x1234] = -1;

        // Assert
        Assert.Equal(0xFF, _memory.UInt8[0x1234]);
        Assert.Equal(0xFF, _memory.UInt8[0x1235]);
        Assert.Equal(0xFF, _memory.UInt8[0x1236]);
        Assert.Equal(0xFF, _memory.UInt8[0x1237]);
    }

    [Fact]
    public void TestMappedGetUint8() {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;
        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);

        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        byte actual = _memory.UInt8[0x1234];

        // Assert
        Assert.Equal(0xDE, actual);
    }

    [Theory]
    [InlineData(0x1232, 0x2312)]
    [InlineData(0x1233, 0xDE23)]
    [InlineData(0x1234, 0xADDE)]
    [InlineData(0x1235, 0x00AD)]
    [InlineData(0x1236, 0x0000)]
    [InlineData(0x1237, 0x7800)]
    [InlineData(0x1238, 0x8978)]
    public void TestMappedGetUint16(ushort testAddress, uint expected) {
        // Arrange
        _memory.UInt8[0x1232] = 0x12;
        _memory.UInt8[0x1233] = 0x23;
        _memory.UInt8[0x1234] = 0x34;
        _memory.UInt8[0x1235] = 0x45;
        _memory.UInt8[0x1236] = 0x56;
        _memory.UInt8[0x1237] = 0x67;
        _memory.UInt8[0x1238] = 0x78;
        _memory.UInt8[0x1239] = 0x89;
        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        newMem.Write(0x1235, 0xAD);

        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        uint actual = _memory.UInt16[testAddress];

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x1234, 0x45342312)]
    [InlineData(0x1235, 0xDE453423)]
    [InlineData(0x1236, 0xADDE4534)]
    [InlineData(0x1237, 0xBEADDE45)]
    [InlineData(0x1238, 0xEFBEADDE)]
    [InlineData(0x1239, 0x9AEFBEAD)]
    [InlineData(0x123A, 0xAB9AEFBE)]
    [InlineData(0x123B, 0xBCAB9AEF)]
    public void TestMappedGetUint32(ushort testAddress, uint expected) {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;
        _memory.UInt8[0x1235] = 0x23;
        _memory.UInt8[0x1236] = 0x34;
        _memory.UInt8[0x1237] = 0x45;
        _memory.UInt8[0x1238] = 0x56;
        _memory.UInt8[0x1239] = 0x67;
        _memory.UInt8[0x123A] = 0x78;
        _memory.UInt8[0x123B] = 0x89;
        _memory.UInt8[0x123C] = 0x9A;
        _memory.UInt8[0x123D] = 0xAB;
        _memory.UInt8[0x123E] = 0xBC;
        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1238, 0xDE);
        newMem.Write(0x1239, 0xAD);
        newMem.Write(0x123A, 0xBE);
        newMem.Write(0x123B, 0xEF);

        _memory.RegisterMapping(0x1238, 4, newMem);

        // Act
        uint actual = _memory.UInt32[testAddress];

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestMappedSetUint8() {
        // Arrange
        _memory.UInt8[0x1234] = 0x34;
        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.UInt8[0x1234] = 0x12;

        // Assert
        Assert.Equal(0x12, newMem.Read(0x1234));
        Assert.Equal(0x12, _memory.UInt8[0x1234]);
    }

    [Theory]
    [InlineData(0x1232, 0xDE, 0xAD)]
    [InlineData(0x1233, 0xBE, 0xAD)]
    [InlineData(0x1234, 0xEF, 0xBE)]
    [InlineData(0x1235, 0xDE, 0xEF)]
    [InlineData(0x1236, 0xDE, 0xAD)]
    public void TestMappedSetUint16(ushort testAddress, byte expected1, byte expected2) {
        // Arrange
        _memory.UInt8[0x1232] = 0x12;
        _memory.UInt8[0x1233] = 0x23;
        _memory.UInt8[0x1234] = 0x34;
        _memory.UInt8[0x1235] = 0x45;
        _memory.UInt8[0x1236] = 0x56;
        _memory.UInt8[0x1237] = 0x67;
        _memory.UInt8[0x1238] = 0x78;
        _memory.UInt8[0x1239] = 0x89;

        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        newMem.Write(0x1235, 0xAD);
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.UInt16[testAddress] = 0xBEEF;

        // Assert
        Assert.Equal(expected1, newMem.Read(0x1234));
        Assert.Equal(expected2, newMem.Read(0x1235));
    }

    
    [Fact]
    public void TestMappedSetUint32() {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;
        _memory.UInt8[0x1235] = 0x23;
        _memory.UInt8[0x1236] = 0x34;
        _memory.UInt8[0x1237] = 0x45;

        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        newMem.Write(0x1235, 0xAD);
        newMem.Write(0x1236, 0xBE);
        newMem.Write(0x1237, 0xEF);
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.UInt32[0x1234] = 0x12345678;

        // Assert
        Assert.Equal(0x78, newMem.Read(0x1234));
        Assert.Equal(0x56, newMem.Read(0x1235));
        Assert.Equal(0x34, newMem.Read(0x1236));
        Assert.Equal(0x12, newMem.Read(0x1237));
        Assert.Equal(0x78, _memory.UInt8[0x1234]);
        Assert.Equal(0x56, _memory.UInt8[0x1235]);
        Assert.Equal(0x34, _memory.UInt8[0x1236]);
        Assert.Equal(0x12, _memory.UInt8[0x1237]);
    }

    [Fact]
    public void TestMappedSetUnalignedUint32() {
        // Arrange
        _memory.UInt8[0x1233] = 0x12;
        _memory.UInt8[0x1234] = 0x23;
        _memory.UInt8[0x1235] = 0x34;
        _memory.UInt8[0x1236] = 0x45;
        _memory.UInt8[0x1237] = 0x56;

        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        newMem.Write(0x1235, 0xAD);
        newMem.Write(0x1236, 0xBE);
        newMem.Write(0x1237, 0xEF);
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.UInt32[0x1233] = 0x12345678;

        // Assert
        Assert.Equal(0x00, newMem.Read(0x1233));
        Assert.Equal(0x56, newMem.Read(0x1234));
        Assert.Equal(0x34, newMem.Read(0x1235));
        Assert.Equal(0x12, newMem.Read(0x1236));
        Assert.Equal(0xEF, newMem.Read(0x1237));
        Assert.Equal(0x78, _memory.UInt8[0x1233]);
        Assert.Equal(0x56, _memory.UInt8[0x1234]);
        Assert.Equal(0x34, _memory.UInt8[0x1235]);
        Assert.Equal(0x12, _memory.UInt8[0x1236]);
        Assert.Equal(0xEF, _memory.UInt8[0x1237]);
    }

    [Fact]
    public void TestMappedSetUint32Overlapping() {
        // Arrange
        _memory.UInt8[0x1234] = 0x12;
        _memory.UInt8[0x1235] = 0x23;
        _memory.UInt8[0x1236] = 0x34;
        _memory.UInt8[0x1237] = 0x45;

        var newMem = new Ram(16 * 1024);
        newMem.Write(0x1234, 0xDE);
        newMem.Write(0x1235, 0xAD);
        newMem.Write(0x1236, 0xBE);
        newMem.Write(0x1237, 0xEF);
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.UInt32[0x1235] = 0x12345678;

        // Assert
        Assert.Equal(0xDE, newMem.Read(0x1234));
        Assert.Equal(0x78, newMem.Read(0x1235));
        Assert.Equal(0x56, newMem.Read(0x1236));
        Assert.Equal(0x34, newMem.Read(0x1237));
        Assert.Equal(0x00, newMem.Read(0x1238));
        Assert.Equal(0xDE, _memory.UInt8[0x1234]);
        Assert.Equal(0x78, _memory.UInt8[0x1235]);
        Assert.Equal(0x56, _memory.UInt8[0x1236]);
        Assert.Equal(0x34, _memory.UInt8[0x1237]);
        Assert.Equal(0x12, _memory.UInt8[0x1238]);
    }
}