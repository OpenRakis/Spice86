namespace Spice86.Tests.CfgCpu.ModRm;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class ModRmExecutorTest {
    private readonly ModRmHelper _modRmHelper = new();
    private const ushort AX = 0x10;
    private const ushort BX = 0x20;
    private const ushort BP = 0x30;
    private const ushort DX = 0x40;
    private const ushort SI = 0x50;
    private const ushort DS = 0x60;
    private const ushort SS = 0x70;

    (ModRmParser, ModRmExecutor) Create() {
        (ModRmParser, ModRmExecutor) res = _modRmHelper.Create();
        _modRmHelper.State.AX = AX;
        _modRmHelper.State.BX = BX;
        _modRmHelper.State.BP = BP;
        _modRmHelper.State.DX = DX;
        _modRmHelper.State.SI = SI;
        _modRmHelper.State.DS = DS;
        _modRmHelper.State.SS = SS;
        return res;
    }

    [Fact]
    public void Execute16Mod0R0Rm0() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0));

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        Assert.Equal(BX + SI, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + BX + SI, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute16Mod0R0Rm110() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0b110),
            // Offset Field
            0x11, 0x22
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        Assert.Equal(0x2211, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + 0x2211, (int)executor.MemoryAddress);
    }
    
    [Fact]
    public void Execute16Mod1R0Rm110() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(1, 0, 0b110),
            //Displacement field
            0x11
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = BP + 0x11;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((SS * 16) + expectedOffset, (int)executor.MemoryAddress);
        
    }
    
    [Fact]
    public void Execute16Mod1R0Rm110NegativeDisplacement() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(1, 0, 0b110),
            //Displacement field (-1)
            0xFF
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = BP - 1;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((SS * 16) + expectedOffset, (int)executor.MemoryAddress);
        
    }

    [Fact]
    public void Execute16Mod2R0Rm110() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(2, 0, 0b110),
            //Displacement field
            0x11, 0x22
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = BP + 0x2211;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((SS * 16) + expectedOffset, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute16Mod3R0Rm0() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(3, 0, 0));

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.WORD_16)));

        // Assert
        Assert.Null(executor.MemoryOffset);
        Assert.Null(executor.MemoryAddress);
    }


    [Fact]
    public void Execute32Mod0R0Rm0() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0));

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        Assert.Equal(AX, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + AX, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute32Mod0R0Rm100() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(0, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3)
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = BX + 2 * DX;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + expectedOffset, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute32Mod0R0Rm100Base5() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(0, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 5),
            // Base field
            0x11, 0x22, 0, 0
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        int expectedOffset = 0x2211 + 2 * DX;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + expectedOffset, (int)executor.MemoryAddress);
    }
    
    [Fact]
    public void Execute32Mod0R0Rm100Base5Fails() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(0, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 5),
            // Base field
            0x11, 0x22, 0x33, 0x44
        );

        // Act
        try {
            executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));
        } catch (CpuGeneralProtectionFaultException e) {
            // Success!
            return;
        }
        // Should have failed with exception since displacement is more than 16 bits
        Assert.Fail();
    }

    [Fact]
    public void Execute32Mod1R0Rm100() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(1, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3),
            // Displacement
            0x11
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = 0x11 + BX + 2 * DX;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + expectedOffset, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute32Mod2R0Rm100() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(2, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3),
            //Displacement field
            0x11, 0x22, 0, 0
        );

        // Act
        executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));

        // Assert
        Assert.NotNull(executor.MemoryOffset);
        ushort expectedOffset = 0x2211 + BX + 2 * DX;
        Assert.Equal(expectedOffset, (int)executor.MemoryOffset);
        Assert.NotNull(executor.MemoryAddress);
        Assert.Equal((DS * 16) + expectedOffset, (int)executor.MemoryAddress);
    }

    [Fact]
    public void Execute32Mod2R0Rm100Fails() {
        // Arrange
        (ModRmParser parser, ModRmExecutor executor) = Create();
        _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(2, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3),
            //Displacement field
            0x11, 0x22, 0x33, 0x44
        );

        // Act
        try {
            executor.RefreshWithNewModRmContext(parser.ParseNext(new TestModRmParsingContext(BitWidth.DWORD_32)));
        } catch (CpuGeneralProtectionFaultException e) {
            // Success!
            return;
        }
        // Should have failed with exception since displacement is more than 16 bits
        Assert.Fail();
    }
}