namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests that the LOCK prefix (0xF0) is validated against the instruction's execution AST.
/// LOCK is only valid when the instruction performs a memory write.
/// Instructions without a memory destination operand must be parsed as invalid (#UD fault).
/// </summary>
public class LockPrefixValidationTest {
    private readonly TestInstructionHelper _helper = new();

    // LOCK ADD [BX], AX: F0 01 07
    // ADD RM16,R16 (opcode 0x01) with ModRM 0x07 (mod=00,reg=AX=000,r/m=BX=111)
    // The RM (memory [BX]) is the destination => memory write => LOCK is valid
    [Fact]
    public void LockAdd_MemoryDestination_ShouldBeValid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0x01); // ADD RM16, R16
            w.WriteUInt8(0x07); // ModRM: [BX], AX
        });

        // Assert
        instruction.IsInvalid.Should().BeFalse("LOCK ADD [BX], AX writes to memory and is a valid LOCK usage");
        instruction.LockPrefix.Should().NotBeNull("the LOCK prefix should be recorded");
    }

    // LOCK MOV AX, [BX]: F0 8B 07
    // MOV R16,RM16 (opcode 0x8B) with ModRM 0x07 (mod=00,reg=AX=000,r/m=BX=111)
    // The destination is the register AX (not memory) => no memory write => LOCK is invalid
    [Fact]
    public void LockMov_RegisterDestination_ShouldBeInvalid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0x8B); // MOV R16, RM16
            w.WriteUInt8(0x07); // ModRM: AX, [BX]
        });

        // Assert
        instruction.IsInvalid.Should().BeTrue("LOCK MOV AX, [BX] reads from memory but writes to a register, which is invalid with LOCK");
    }

    // LOCK NOP: F0 90
    // NOP does nothing (no memory access at all) => LOCK is invalid
    [Fact]
    public void LockNop_ShouldBeInvalid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0x90); // NOP
        });

        // Assert
        instruction.IsInvalid.Should().BeTrue("LOCK NOP has no memory operand at all and must generate #UD");
    }

    // LOCK INC [BX]: F0 FF 07
    // INC RM16 via GRP5 (opcode 0xFF, ModRM 0x07 = mod=00,reg=0=INC,r/m=BX=111)
    // [BX] is the destination => memory write => LOCK is valid
    [Fact]
    public void LockInc_MemoryDestination_ShouldBeValid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0xFF); // GRP5
            w.WriteUInt8(0x07); // ModRM: INC [BX]
        });

        // Assert
        instruction.IsInvalid.Should().BeFalse("LOCK INC [BX] writes to memory and is a valid LOCK usage");
        instruction.LockPrefix.Should().NotBeNull("the LOCK prefix should be recorded");
    }

    // LOCK XCHG AX, [BX]: F0 87 07
    // XCHG R16, RM16 (opcode 0x87) with ModRM 0x07 (mod=00,reg=AX=000,r/m=BX=111)
    // Both R and RM are written; RM=[BX] is memory => LOCK is valid
    [Fact]
    public void LockXchg_MemoryOperand_ShouldBeValid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0x87); // XCHG R16, RM16
            w.WriteUInt8(0x07); // ModRM: AX, [BX]
        });

        // Assert
        instruction.IsInvalid.Should().BeFalse("LOCK XCHG AX, [BX] writes to memory and is a valid LOCK usage");
        instruction.LockPrefix.Should().NotBeNull("the LOCK prefix should be recorded");
    }

    // LOCK MOV [BX], AX: F0 89 07
    // MOV RM16, R16 (opcode 0x89) with ModRM 0x07 (mod=00,reg=AX=000,r/m=BX=111)
    // MOV writes to memory but is NOT in the Intel LOCK-allowed instruction set => #UD
    [Fact]
    public void LockMov_MemoryDestination_ShouldBeInvalid() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xF0); // LOCK prefix
            w.WriteUInt8(0x89); // MOV RM16, R16
            w.WriteUInt8(0x07); // ModRM: [BX], AX
        });

        // Assert
        instruction.IsInvalid.Should().BeTrue("LOCK MOV [BX], AX writes to memory but MOV is not in the Intel LOCK-allowed instruction set");
    }
}
