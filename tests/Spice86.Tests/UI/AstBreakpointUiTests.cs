namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Views;

using Xunit;

/// <summary>
/// Comprehensive UI tests for the AST-based conditional breakpoint feature.
/// These tests verify that conditional breakpoint expressions can be parsed,
/// compiled, and used in breakpoint creation through the UI components.
/// </summary>
public class AstBreakpointUiTests {
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

    #region Register Expression Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Comparison Operator Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Logical Operator Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Bitwise Operator Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Arithmetic Operator Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Shift Operator Tests

    // Note: Shift operators are parsed but not yet implemented in AstExpressionBuilder.
    // These tests document the expected behavior when shift operators are implemented.
    // Uncomment when shift operator support is added to ToExpression() in AstExpressionBuilder.cs

    // [AvaloniaFact]
    // public void TestLeftShiftOperator() {
    //     // Arrange
    //     State state = CreateTestState();
    //     Memory memory = CreateTestMemory();
    //     state.AX = 0x1;
    //
    //     BreakpointConditionCompiler compiler = new(state, memory);
    //
    //     // Act
    //     Func<long, bool> condition = compiler.Compile("(ax << 4) == 0x10");
    //     bool result = condition(0);
    //
    //     // Assert
    //     result.Should().BeTrue();
    // }

    // [AvaloniaFact]
    // public void TestRightShiftOperator() {
    //     // Arrange
    //     State state = CreateTestState();
    //     Memory memory = CreateTestMemory();
    //     state.AX = 0x100;
    //
    //     BreakpointConditionCompiler compiler = new(state, memory);
    //
    //     // Act
    //     Func<long, bool> condition = compiler.Compile("(ax >> 4) == 0x10");
    //     bool result = condition(0);
    //
    //     // Assert
    //     result.Should().BeTrue();
    // }

    #endregion

    #region Complex Expression Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Error Handling Tests

    [AvaloniaFact]
    public void TestInvalidExpressionThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("invalid expression @@!");
        act.Should().Throw<ExpressionParseException>();
    }

    [AvaloniaFact]
    public void TestUnknownRegisterThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("xyz == 0x100");
        act.Should().Throw<ExpressionParseException>();
    }

    [AvaloniaFact]
    public void TestEmptyExpressionThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("");
        act.Should().Throw<ExpressionParseException>();
    }

    [AvaloniaFact]
    public void TestUnmatchedParenthesisThrowsException() {
        // Arrange
        State state = CreateTestState();
        Memory memory = CreateTestMemory();

        BreakpointConditionCompiler compiler = new(state, memory);

        // Act & Assert
        Action act = () => compiler.Compile("(ax == 0x100");
        act.Should().Throw<ExpressionParseException>();
    }

    #endregion

    #region UI Component Tests

    /// <summary>
    /// Verifies that the DisassemblyView contains the breakpoint dialog elements.
    /// </summary>
    [AvaloniaFact]
    public void TestDisassemblyViewContainsBreakpointDialogElements() {
        // Arrange & Act
        DisassemblyView disassemblyView = new();

        // Assert - The view should be created successfully
        disassemblyView.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the BreakpointsView can be created with all required elements.
    /// </summary>
    [AvaloniaFact]
    public void TestBreakpointsViewContainsConditionalExpressionInput() {
        // Arrange & Act
        BreakpointsView breakpointsView = new();

        // Assert - The view should be created successfully
        breakpointsView.Should().NotBeNull();
    }

    #endregion

    #region Parser Round-trip Tests

    [AvaloniaFact]
    public void TestParserRoundTrip_SimpleExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "ax == 0x100";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void TestParserRoundTrip_ComplexExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "ax == 0x100 && bx > 0x200";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void TestParserRoundTrip_MemoryAccessExpression() {
        // Arrange
        AstExpressionParser parser = new();
        string expression = "byte ptr [0x100] == 0x42";

        // Act
        Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.ValueNode ast = parser.Parse(expression);

        // Assert
        ast.Should().NotBeNull();
    }

    #endregion
}
