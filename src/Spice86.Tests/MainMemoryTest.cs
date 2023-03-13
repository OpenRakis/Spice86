namespace Spice86.Tests;

using Spice86.Core.Emulator.Memory;

using Xunit;

public class MainMemoryTest {
    private readonly MainMemory _memory = new(64);

    [Fact]
    public void TestGetUint8() {
        // Arrange
        _memory.Ram[0x1234] = 0x12;

        // Act
        byte actual = _memory.GetUint8(0x1234);
        
        // Assert
        Assert.Equal(0x12, actual);
    }
    
    [Fact]
    public void TestGetUint16() {
        // Arrange
        _memory.Ram[0x1234] = 0x34;
        _memory.Ram[0x1235] = 0x12;

        // Act
        ushort actual = _memory.GetUint16(0x1234);
        
        // Assert
        Assert.Equal(0x1234, actual);
    }
    
    [Fact]
    public void TestGetUint32() {
        // Arrange
        _memory.Ram[0x1234] = 0x34;
        _memory.Ram[0x1235] = 0x12;
        _memory.Ram[0x1236] = 0x78;
        _memory.Ram[0x1237] = 0x56;

        // Act
        uint actual = _memory.GetUint32(0x1234);
        
        // Assert
        Assert.Equal(0x56781234u, actual);
    }
    
    [Fact]
    public void TestSetUint8() {
        // Arrange
        _memory.Ram[0x1234] = 0x00;

        // Act
        _memory.SetUint8(0x1234, 0x12);
        
        // Assert
        Assert.Equal(0x12, _memory.Ram[0x1234]);
    }
    
    [Fact]
    public void TestSetUint16() {
        // Arrange
        _memory.Ram[0x1234] = 0x00;
        _memory.Ram[0x1235] = 0x00;

        // Act
        _memory.SetUint16(0x1234, 0x1234);
        
        // Assert
        Assert.Equal(0x34, _memory.Ram[0x1234]);
        Assert.Equal(0x12, _memory.Ram[0x1235]);
    }
    
    [Fact]
    public void TestSetUint32() {
        // Arrange
        _memory.Ram[0x1234] = 0x00;
        _memory.Ram[0x1235] = 0x00;
        _memory.Ram[0x1236] = 0x00;
        _memory.Ram[0x1237] = 0x00;

        // Act
        _memory.SetUint32(0x1234, 0x56781234u);
        
        // Assert
        Assert.Equal(0x34, _memory.Ram[0x1234]);
        Assert.Equal(0x12, _memory.Ram[0x1235]);
        Assert.Equal(0x78, _memory.Ram[0x1236]);
        Assert.Equal(0x56, _memory.Ram[0x1237]);
    }
    
    [Fact]
    public void TestMappedGetUint8() {
        // Arrange
        _memory.Ram[0x1234] = 0x12;
        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE
            }
        };

        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        byte actual = _memory.GetUint8(0x1234);
        
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
        _memory.Ram[0x1232] = 0x12;
        _memory.Ram[0x1233] = 0x23;
        _memory.Ram[0x1234] = 0x34;
        _memory.Ram[0x1235] = 0x45;
        _memory.Ram[0x1236] = 0x56;
        _memory.Ram[0x1237] = 0x67;
        _memory.Ram[0x1238] = 0x78;
        _memory.Ram[0x1239] = 0x89;
        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE,
                [0x1235] = 0xAD,
            }
        };

        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        uint actual = _memory.GetUint16(testAddress);
        
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
        _memory.Ram[0x1234] = 0x12;
        _memory.Ram[0x1235] = 0x23;
        _memory.Ram[0x1236] = 0x34;
        _memory.Ram[0x1237] = 0x45;
        _memory.Ram[0x1238] = 0x56;
        _memory.Ram[0x1239] = 0x67;
        _memory.Ram[0x123A] = 0x78;
        _memory.Ram[0x123B] = 0x89;
        _memory.Ram[0x123C] = 0x9A;
        _memory.Ram[0x123D] = 0xAB;
        _memory.Ram[0x123E] = 0xBC;
        var newMem = new Memory(16) {
            Ram = {
                [0x1238] = 0xDE,
                [0x1239] = 0xAD,
                [0x123A] = 0xBE,
                [0x123B] = 0xEF,
            }
        };

        _memory.RegisterMapping(0x1238, 4, newMem);

        // Act
        uint actual = _memory.GetUint32(testAddress);
        
        // Assert
        Assert.Equal(expected, actual);
    }
    
    [Fact]
    public void TestMappedSetUint8() {
        // Arrange
        _memory.Ram[0x1234] = 0x34;
        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE
            }
        };
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.SetUint8(0x1234, 0x12);
        
        // Assert
        Assert.Equal(0x12, newMem.Ram[0x1234]);
        Assert.Equal(0x34, _memory.Ram[0x1234]);
    }
    
    [Theory]
    [InlineData(0x1232, 0xDE, 0xAD)]
    [InlineData(0x1233, 0xBE, 0xAD)]
    [InlineData(0x1234, 0xEF, 0xBE)]
    [InlineData(0x1235, 0xDE, 0xEF)]
    [InlineData(0x1236, 0xDE, 0xAD)]
    public void TestMappedSetUint16(ushort testAddress, byte expected1, byte expected2) {
        // Arrange
        _memory.Ram[0x1232] = 0x12;
        _memory.Ram[0x1233] = 0x23;
        _memory.Ram[0x1234] = 0x34;
        _memory.Ram[0x1235] = 0x45;
        _memory.Ram[0x1236] = 0x56;
        _memory.Ram[0x1237] = 0x67;
        _memory.Ram[0x1238] = 0x78;
        _memory.Ram[0x1239] = 0x89;
        
        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE,
                [0x1235] = 0xAD,
            }
        };
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.SetUint16(testAddress, 0xBEEF);
        
        // Assert
        Assert.Equal(expected1, newMem.Ram[0x1234]);
        Assert.Equal(expected2, newMem.Ram[0x1235]);
    }

    
    [Fact]
    public void TestMappedSetUint32() {
        // Arrange
        _memory.Ram[0x1234] = 0x12;
        _memory.Ram[0x1235] = 0x23;
        _memory.Ram[0x1236] = 0x34;
        _memory.Ram[0x1237] = 0x45;

        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE,
                [0x1235] = 0xAD,
                [0x1236] = 0xBE,
                [0x1237] = 0xEF,
            }
        };
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.SetUint32(0x1234, 0x12345678);

        // Assert
        Assert.Equal(0x78, newMem.Ram[0x1234]);
        Assert.Equal(0x56, newMem.Ram[0x1235]);
        Assert.Equal(0x34, newMem.Ram[0x1236]);
        Assert.Equal(0x12, newMem.Ram[0x1237]);
        Assert.Equal(0x12, _memory.Ram[0x1234]);
        Assert.Equal(0x23, _memory.Ram[0x1235]);
        Assert.Equal(0x34, _memory.Ram[0x1236]);
        Assert.Equal(0x45, _memory.Ram[0x1237]);
    }
    
    [Fact]
    public void TestMappedSetUnalignedUint32() {
        // Arrange
        _memory.Ram[0x1233] = 0x12;
        _memory.Ram[0x1234] = 0x23;
        _memory.Ram[0x1235] = 0x34;
        _memory.Ram[0x1236] = 0x45;
        _memory.Ram[0x1237] = 0x56;

        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE,
                [0x1235] = 0xAD,
                [0x1236] = 0xBE,
                [0x1237] = 0xEF,
            }
        };
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.SetUint32(0x1233, 0x12345678);

        // Assert
        Assert.Equal(0x00, newMem.Ram[0x1233]);
        Assert.Equal(0x56, newMem.Ram[0x1234]);
        Assert.Equal(0x34, newMem.Ram[0x1235]);
        Assert.Equal(0x12, newMem.Ram[0x1236]);
        Assert.Equal(0xEF, newMem.Ram[0x1237]);
        Assert.Equal(0x78, _memory.Ram[0x1233]);
        Assert.Equal(0x23, _memory.Ram[0x1234]);
        Assert.Equal(0x34, _memory.Ram[0x1235]);
        Assert.Equal(0x45, _memory.Ram[0x1236]);
        Assert.Equal(0x56, _memory.Ram[0x1237]);
    }
    
    [Fact]
    public void TestMappedSetUint32Overlapping() {
        // Arrange
        _memory.Ram[0x1234] = 0x12;
        _memory.Ram[0x1235] = 0x23;
        _memory.Ram[0x1236] = 0x34;
        _memory.Ram[0x1237] = 0x45;

        var newMem = new Memory(16) {
            Ram = {
                [0x1234] = 0xDE,
                [0x1235] = 0xAD,
                [0x1236] = 0xBE,
                [0x1237] = 0xEF,
            }
        };
        _memory.RegisterMapping(0x1234, 4, newMem);

        // Act
        _memory.SetUint32(0x1235, 0x12345678);

        // Assert
        Assert.Equal(0xDE, newMem.Ram[0x1234]);
        Assert.Equal(0x78, newMem.Ram[0x1235]);
        Assert.Equal(0x56, newMem.Ram[0x1236]);
        Assert.Equal(0x34, newMem.Ram[0x1237]);
        Assert.Equal(0x00, newMem.Ram[0x1238]);
        Assert.Equal(0x12, _memory.Ram[0x1234]);
        Assert.Equal(0x23, _memory.Ram[0x1235]);
        Assert.Equal(0x34, _memory.Ram[0x1236]);
        Assert.Equal(0x45, _memory.Ram[0x1237]);
        Assert.Equal(0x12, _memory.Ram[0x1238]);
    }
}