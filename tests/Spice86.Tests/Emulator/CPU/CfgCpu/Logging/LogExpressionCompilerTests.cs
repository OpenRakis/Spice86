namespace Spice86.Tests.Emulator.CPU.CfgCpu.Logging;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// End-to-end tests for <see cref="LogExpressionCompiler"/>: parse a named expression, compile it,
/// then evaluate it against live <see cref="State"/> and <see cref="Memory"/> instances.
/// Covers register access, arithmetic, boolean, absolute/segmented memory, dynamic re-evaluation,
/// multiple expressions, and invalid-format error handling.
/// </summary>
public class LogExpressionCompilerTests {
    private readonly State _state;
    private readonly Memory _memory;
    private readonly LogExpressionCompiler _compiler;

    public LogExpressionCompilerTests() {
        _state = new State(CpuModel.INTEL_80286);
        AddressReadWriteBreakpoints breakpoints = new();
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new();
        _memory = new Memory(breakpoints, ram, a20Gate, new RealModeMmu386(), false);
        _compiler = new LogExpressionCompiler(_state, _memory);
    }

    private long Evaluate(string namedExpression) {
        CompiledLogExpression compiled = _compiler.Compile(namedExpression);
        return compiled.Evaluate();
    }

    // --- Register expressions ---

    [Fact]
    public void TestRegister_AxPlusOne() {
        _state.AX = 0x0041;
        long result = Evaluate("life=AX+1");
        result.Should().Be(0x42);
    }

    [Fact]
    public void TestRegister_Ebx() {
        _state.EBX = 0xDEADBEEF;
        long result = Evaluate("raw=EBX");
        result.Should().Be((long)0xDEADBEEFu);
    }

    [Fact]
    public void TestRegister_SegmentDs() {
        _state.DS = 0x01DD;
        long result = Evaluate("seg=DS");
        result.Should().Be(0x01DD);
    }

    // --- Arithmetic expressions ---

    [Fact]
    public void TestArithmetic_Sum() {
        _state.AX = 3;
        _state.BX = 5;
        long result = Evaluate("sum=AX+BX");
        result.Should().Be(8);
    }

    [Fact]
    public void TestArithmetic_LeftShift() {
        _state.AX = 0x0001;
        long result = Evaluate("shifted=AX<<2");
        result.Should().Be(4);
    }

    [Fact]
    public void TestArithmetic_BitwiseAnd() {
        _state.EAX = 0xDEADBEEF;
        long result = Evaluate("masked=EAX&0xFF");
        result.Should().Be(0xEF);
    }

    // --- Boolean expressions (result is 0 or 1) ---

    [Fact]
    public void TestBoolean_AxEqualsZero_True() {
        _state.AX = 0;
        long result = Evaluate("flag=AX==0");
        result.Should().Be(1);
    }

    [Fact]
    public void TestBoolean_AxEqualsZero_False() {
        _state.AX = 1;
        long result = Evaluate("flag=AX==0");
        result.Should().Be(0);
    }

    // --- Absolute memory access ---

    [Fact]
    public void TestAbsoluteMemory_Byte() {
        _memory.UInt8[0x1234u] = 0xAB;
        long result = Evaluate("b=byte ptr [0x1234]");
        result.Should().Be(0xAB);
    }

    [Fact]
    public void TestAbsoluteMemory_Word() {
        _memory.UInt16[0x1000u] = 0x1234;
        long result = Evaluate("w=word ptr [0x1000]");
        result.Should().Be(0x1234);
    }

    [Fact]
    public void TestAbsoluteMemory_Dword() {
        _memory.UInt32[0x2000u] = 0x12345678u;
        long result = Evaluate("d=dword ptr [0x2000]");
        result.Should().Be(0x12345678);
    }

    [Fact]
    public void TestAbsoluteMemory_EaxUsesFullAddress() {
        // EAX = 0x10000 which is above 0xFFFF: absolute pointer must use all 32 bits
        _memory.UInt8[0x10000u] = 0x55;
        _state.EAX = 0x10000;
        long result = Evaluate("v=byte ptr [eax]");
        result.Should().Be(0x55);
    }

    // --- Segmented memory access ---

    [Fact]
    public void TestSegmentedMemory_ByteWithDsRegisterAliasForLiteralSegment() {
        _state.DS = 0x01DD;
        _memory.UInt8[(ushort)0x01DD, (ushort)0x0100] = 0xFF;
        long result = Evaluate("sb=byte ptr ds:[0x0100]");
        result.Should().Be(0xFF);
    }

    [Fact]
    public void TestSegmentedMemory_WordWithDsRegister() {
        _state.DS = 0x01DD;
        _memory.UInt16[(ushort)0x01DD, (ushort)0x0200] = 0xBEEF;
        long result = Evaluate("sw=word ptr ds:[0x0200]");
        result.Should().Be(0xBEEF);
    }

    [Fact]
    public void TestSegmentedMemory_DwordWithEsRegister() {
        _state.ES = 0x01DD;
        _memory.UInt32[(ushort)0x01DD, (ushort)0x0300] = 0xDEADBEEFu;
        long result = Evaluate("sd=dword ptr es:[0x0300]");
        result.Should().Be((long)0xDEADBEEFu);
    }

    [Fact]
    public void TestSegmentedMemory_EaxAsOffsetTruncatedTo16Bits() {
        // EAX = 0x00010020: upper 16 bits must be discarded; only 0x0020 is used as offset
        _state.DS = 0x0100;
        _state.EAX = 0x00010020;
        _memory.UInt8[(ushort)0x0100, 0x0020] = 0x7E;
        // Sanity: ensure the "wrong" address (using full 32-bit value as absolute) holds nothing
        _memory.UInt8[0x10020u] = 0x00;
        long result = Evaluate("v=byte ptr ds:[eax]");
        result.Should().Be(0x7E);
    }

    // --- Dynamic evaluation: compiled once, evaluated after state/memory changes ---

    [Fact]
    public void TestDynamic_RegisterChangesAfterCompile() {
        _state.AX = 0x0010;
        CompiledLogExpression compiled = _compiler.Compile("val=AX");

        compiled.Evaluate().Should().Be(0x0010);

        _state.AX = 0x0020;
        compiled.Evaluate().Should().Be(0x0020);
    }

    [Fact]
    public void TestDynamic_MemoryChangesAfterCompile() {
        CompiledLogExpression compiled = _compiler.Compile("mem=byte ptr [0x5000]");

        _memory.UInt8[0x5000u] = 0x11;
        compiled.Evaluate().Should().Be(0x11);

        _memory.UInt8[0x5000u] = 0x22;
        compiled.Evaluate().Should().Be(0x22);
    }

    [Fact]
    public void TestDynamic_TwoDistinctMemoryAddresses() {
        _memory.UInt8[0x3000u] = 0xAA;
        _memory.UInt8[0x3001u] = 0xBB;

        CompiledLogExpression compiledA = _compiler.Compile("a=byte ptr [0x3000]");
        CompiledLogExpression compiledB = _compiler.Compile("b=byte ptr [0x3001]");

        compiledA.Evaluate().Should().Be(0xAA);
        compiledB.Evaluate().Should().Be(0xBB);
    }

    [Fact]
    public void TestDynamic_RegisterAsPointerChangesAfterCompile() {
        _memory.UInt8[0x4000u] = 0xCC;
        _memory.UInt8[0x4001u] = 0xDD;
        _state.BX = 0x4000;
        CompiledLogExpression compiled = _compiler.Compile("ptr=byte ptr [bx]");

        compiled.Evaluate().Should().Be(0xCC);

        _state.BX = 0x4001;
        compiled.Evaluate().Should().Be(0xDD);
    }

    [Fact]
    public void TestAbsolutePointer_ComputedAddress_RegisterPlusOffset() {
        // byte ptr [bx+1]: address is BX+1, which is a UINT32 arithmetic result
        _memory.UInt8[0x6001u] = 0xEE;
        _state.BX = 0x6000;
        long result = Evaluate("v=byte ptr [bx+1]");
        result.Should().Be(0xEE);
    }

    [Fact]
    public void TestAbsolutePointer_ComputedAddress_MultiplyExpression() {
        // word ptr [ax*2]: AX=0x100 → address 0x200
        _memory.UInt16[0x0200u] = 0xCAFE;
        _state.AX = 0x0100;
        long result = Evaluate("v=word ptr [ax*2]");
        result.Should().Be(0xCAFE);
    }

    [Fact]
    public void TestSegmentedPointer_ComputedOffset_RegisterPlusConstant() {
        // word ptr ds:[bx+4]: DS=0x0100, BX=0x10 → physical 0x1014
        _state.DS = 0x0100;
        _state.BX = 0x0010;
        _memory.UInt16[(ushort)0x0100, (ushort)0x0014] = 0xABCD;
        long result = Evaluate("v=word ptr ds:[bx+4]");
        result.Should().Be(0xABCD);
    }

    [Fact]
    public void TestSegmentedPointer_ComputedOffset_Arithmetic() {
        // byte ptr es:[ax*3+1]: ES=0x0200, AX=0x0010 → offset=0x31, physical 0x2031
        _state.ES = 0x0200;
        _state.AX = 0x0010;
        _memory.UInt8[(ushort)0x0200, (ushort)0x0031] = 0x7F;
        long result = Evaluate("v=byte ptr es:[ax*3+1]");
        result.Should().Be(0x7F);
    }

    [Fact]
    public void TestSegmentedPointer_DynamicSegmentAndOffset() {
        // dword ptr ss:[si]: both segment and offset are register-driven, change after compile
        _state.SS = 0x0300;
        _state.SI = 0x0010;
        _memory.UInt32[(ushort)0x0300, (ushort)0x0010] = 0x11223344u;
        CompiledLogExpression compiled = _compiler.Compile("v=dword ptr ss:[si]");

        compiled.Evaluate().Should().Be(0x11223344);

        _state.SI = 0x0014;
        _memory.UInt32[(ushort)0x0300, (ushort)0x0014] = 0x55667788u;
        compiled.Evaluate().Should().Be(0x55667788);
    }

    // --- Multiple expressions ---

    [Fact]
    public void TestMultiple_NamesAndValuesAreIndependent() {
        _state.AX = 0x1111;
        _state.BX = 0x2222;

        CompiledLogExpression exprA = _compiler.Compile("a=AX");
        CompiledLogExpression exprB = _compiler.Compile("b=BX");

        exprA.Name.Should().Be("a");
        exprB.Name.Should().Be("b");
        exprA.Evaluate().Should().Be(0x1111);
        exprB.Evaluate().Should().Be(0x2222);
    }

    // --- Parsing edge cases ---

    [Fact]
    public void TestParsing_EqualsInRhsSplitsOnFirstEquals() {
        _state.AX = 0;
        CompiledLogExpression compiled = _compiler.Compile("flag=AX==0");
        compiled.Name.Should().Be("flag");
        compiled.Evaluate().Should().Be(1);
    }

    [Fact]
    public void TestParsing_NoEquals_ThrowsArgumentException() {
        Action act = () => _compiler.Compile("missingequals");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TestParsing_EmptyName_ThrowsArgumentException() {
        Action act = () => _compiler.Compile("=AX");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TestParsing_BareSquareBracketWithoutPtrKeyword_ThrowsExpressionParseException() {
        // [0x2000] without a size qualifier (byte/word/dword ptr) is not valid syntax:
        // the parser does not know what size to read, so it must reject the bare '['.
        Action act = () => _compiler.Compile("v=[0x2000]");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void TestParsing_SegmentedPointerWithoutPtrKeyword_ThrowsExpressionParseException() {
        // DS:[AX] without a size qualifier silently parsed as just DS (the register)
        // with :[AX] left over. The parser must reject trailing unconsumed input.
        Action act = () => _compiler.Compile("problem=DS:[AX]");
        act.Should().Throw<ExpressionParseException>();
    }
}
