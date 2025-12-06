namespace Spice86.Tests.Emulator.CPU.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Comprehensive tests for the BreakpointConditionCompiler and AstExpressionParser.
/// These tests verify that conditional breakpoint expressions can be parsed,
/// compiled, and executed correctly.
/// </summary>
public class BreakpointConditionCompilerTests {
    /// <summary>
    /// Creates a test State with initial register values for testing.
    /// </summary>
    private static State CreateTestState() {
        State state = new(CpuModel.INTEL_80286);
        state.AX = 0x100;
        state.BX = 0x200;
        state.CX = 0x300;
        state.DX = 0x400;
        state.CS = 0x1000;
        state.DS = 0x2000;
        state.ES = 0x3000;
        state.SS = 0x4000;
        state.SP = 0xFFFE;
        state.IP = 0x0100;
        return state;
    }

    /// <summary>
    /// Creates a test Memory with some test data.
    /// </summary>
    private static Memory CreateTestMemory() {
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);

        // Set up some test data in memory
        memory.UInt8[0x100] = 0x42;
        memory.UInt8[0x101] = 0x43;
        memory.UInt16[0x200] = 0x1234;
        memory.UInt32[0x300] = 0xDEADBEEF;

        return memory;
    }

    [Fact]
    public void TestConditionWithAx_WhenAxMatches_ReturnsTrue() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x1234;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x1234");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestConditionWithAx_WhenAxDoesNotMatch_ReturnsFalse() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x5678;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x1234");
        bool result = condition(0);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TestConditionWith8BitRegisters_Al() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x1234; // AL = 0x34, AH = 0x12

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("al == 0x34");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestConditionWith8BitRegisters_Ah() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x1234; // AL = 0x34, AH = 0x12

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ah == 0x12");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestConditionWith32BitRegisters_Eax() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.EAX = 0xDEADBEEF;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("eax == 0xDEADBEEF");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestConditionWithSegmentRegisters() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.DS = 0x5000;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ds == 0x5000");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestNotEqualOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax != 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestLessThanOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax < 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestGreaterThanOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x300;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax > 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestLessThanOrEqualOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> conditionLess = compiler.Compile("ax <= 0x100");
        bool resultEqual = conditionLess(0);

        // Assert
        resultEqual.Should().BeTrue();
    }

    [Fact]
    public void TestGreaterThanOrEqualOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> conditionEqual = compiler.Compile("ax >= 0x100");
        bool resultEqual = conditionEqual(0);

        // Assert
        resultEqual.Should().BeTrue();
    }

    [Fact]
    public void TestLogicalAndOperator_BothTrue() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;
        state.BX = 0x200;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x100 && bx == 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestLogicalAndOperator_OneFalse() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;
        state.BX = 0x300; // Different from 0x200

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x100 && bx == 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TestLogicalOrOperator_OneTrue() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;
        state.BX = 0x999; // Different from 0x200

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x100 || bx == 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestLogicalOrOperator_BothFalse() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x999;
        state.BX = 0x999;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 0x100 || bx == 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TestLogicalNotOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("!(ax == 0x200)");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestBitwiseAndOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0xFF00;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax & 0xFF00) == 0xFF00");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestBitwiseOrOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x00FF;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax | 0xFF00) == 0xFFFF");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestBitwiseXorOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0xAAAA;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax ^ 0xFFFF) == 0x5555");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestBitwiseNotOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(~ax & 0xFFFF) == 0xFFFF");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestAdditionOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax + 0x100) == 0x200");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestSubtractionOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x200;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax - 0x100) == 0x100");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestMultiplicationOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x10;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax * 0x10) == 0x100");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestDivisionOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax / 0x10) == 0x10");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestModuloOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x105;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax % 0x10) == 0x5");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestLeftShiftOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x1;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax << 4) == 0x10");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestRightShiftOperator() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax >> 4) == 0x10");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestComplexNestedExpression() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x100;
        state.BX = 0x200;
        state.CX = 0x300;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act - Complex expression with multiple operators and parentheses
        Func<long, bool> condition = compiler.Compile("(ax == 0x100 && bx == 0x200) || cx > 0x400");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestExpressionWithMultipleRegisters() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 0x10;
        state.BX = 0x20;
        state.CX = 0x30;

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("(ax + bx + cx) == 0x60");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestExpressionWithDecimalNumbers() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();
        state.AX = 256; // Decimal 256 = 0x100

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act
        Func<long, bool> condition = compiler.Compile("ax == 256");
        bool result = condition(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TestInvalidExpressionThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("invalid expression @@!");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void TestUnknownRegisterThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("xyz == 0x100");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void TestEmptyExpressionThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void TestUnmatchedParenthesisThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("(ax == 0x100");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void TestParserRoundTrip_SimpleExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "ax == 0x100";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }

    [Fact]
    public void TestParserRoundTrip_ComplexExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "ax == 0x100 && bx > 0x200";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }

    [Fact]
    public void TestParserRoundTrip_MemoryAccessExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "byte ptr [0x100] == 0x42";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }
}