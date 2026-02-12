namespace Spice86.Tests.Emulator.CPU.CfgCpu.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Xunit;

/// <summary>
/// Tests to verify that AstExpressionParser and AstInstructionRenderer work together correctly.
/// These tests ensure that expressions can be parsed into AST and rendered back to strings.
/// 
/// The round-trip applies the following normalizations (by design):
/// 1. <b>Case normalization</b>: Registers are rendered in uppercase (ax → AX)
/// 2. <b>Zero-padding</b>: Hex constants are zero-padded based on data type (0x100 → 0x00000100 for 32-bit)
/// 3. <b>Type safety</b>: Type conversions are added when comparing different-width operands ((uint)AX when comparing 16-bit AX to 32-bit constant)
/// 4. <b>Semantic representation</b>: Redundant parentheses are removed as the AST represents semantic meaning
/// 5. <b>Precedence preservation</b>: Parentheses are added back only when needed for operator precedence
/// 
/// These normalizations ensure semantic equivalence while producing consistent, unambiguous output.
/// </summary>
public class AstExpressionParserRoundTripTest {
    private readonly AstExpressionParser _parser = new();
    private readonly AstInstructionRenderer _renderer = new(AsmRenderingConfig.CreateSpice86Style());

    private void AssertRoundTrip(string expression, string? expected = null) {
        // Parse expression into AST
        ValueNode ast = _parser.Parse(expression);

        // Render AST back to string
        string rendered = ast.Accept(_renderer);

        // Verify rendered string matches expected (or original if expected not provided)
        string expectedResult = expected ?? expression;
        Assert.Equal(expectedResult, rendered);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("9")]
    [InlineData("0x00", "0")]
    [InlineData("0x01", "1")]
    [InlineData("0x0A", "0x0000000A")]
    [InlineData("0x42", "0x00000042")]
    [InlineData("0xFF", "0x000000FF")]
    [InlineData("0x100", "0x00000100")]
    [InlineData("0x1234", "0x00001234")]
    [InlineData("0xFFFF", "0x0000FFFF")]
    [InlineData("0x12345678", "0x12345678")]
    public void TestConstants(string input, string? expected = null) {
        AssertRoundTrip(input, expected);
    }

    [Theory]
    [InlineData("al", "AL")]
    [InlineData("ah", "AH")]
    [InlineData("bl", "BL")]
    [InlineData("bh", "BH")]
    [InlineData("cl", "CL")]
    [InlineData("ch", "CH")]
    [InlineData("dl", "DL")]
    [InlineData("dh", "DH")]
    [InlineData("ax", "AX")]
    [InlineData("bx", "BX")]
    [InlineData("cx", "CX")]
    [InlineData("dx", "DX")]
    [InlineData("si", "SI")]
    [InlineData("di", "DI")]
    [InlineData("sp", "SP")]
    [InlineData("bp", "BP")]
    [InlineData("eax", "EAX")]
    [InlineData("ebx", "EBX")]
    [InlineData("ecx", "ECX")]
    [InlineData("edx", "EDX")]
    [InlineData("esi", "ESI")]
    [InlineData("edi", "EDI")]
    [InlineData("esp", "ESP")]
    [InlineData("ebp", "EBP")]
    public void TestRegisters(string register, string? expected = null) {
        AssertRoundTrip(register, expected);
    }

    [Theory]
    [InlineData("es", "ES")]
    [InlineData("cs", "CS")]
    [InlineData("ss", "SS")]
    [InlineData("ds", "DS")]
    [InlineData("fs", "FS")]
    [InlineData("gs", "GS")]
    public void TestSegmentRegisters(string register, string? expected = null) {
        AssertRoundTrip(register, expected);
    }

    [Theory]
    [InlineData("ax+bx", "AX+BX")]
    [InlineData("ax+0x100", "AX+0x00000100")]
    [InlineData("0x100+ax", "0x00000100+AX")]
    [InlineData("ax+bx+cx", "AX+BX+CX")]
    public void TestAddition(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax-bx", "AX-BX")]
    [InlineData("ax-0x100", "AX-0x00000100")]
    [InlineData("0x100-ax", "0x00000100-AX")]
    public void TestSubtraction(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax*bx", "AX*BX")]
    [InlineData("ax*2", "AX*2")]
    [InlineData("3*ax", "3*AX")]
    public void TestMultiplication(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax/bx", "AX/BX")]
    [InlineData("ax/2", "AX/2")]
    [InlineData("0x100/ax", "0x00000100/AX")]
    public void TestDivision(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax%bx", "AX%BX")]
    [InlineData("ax%2", "AX%2")]
    [InlineData("0x100%ax", "0x00000100%AX")]
    public void TestModulo(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax==bx", "AX==BX")]
    [InlineData("ax==0x100", "(uint)AX==0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("al==0x42", "(uint)AL==0x00000042")]  // Type conversion: AL (8-bit) to uint (32-bit)
    public void TestEquality(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax!=bx", "AX!=BX")]
    [InlineData("ax!=0x100", "(uint)AX!=0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("al!=0x42", "(uint)AL!=0x00000042")]  // Type conversion: AL (8-bit) to uint (32-bit)
    public void TestInequality(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax<bx", "AX<BX")]
    [InlineData("ax<0x100", "(uint)AX<0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("0x100<ax", "0x00000100<(uint)AX")]  // Type conversion: AX (16-bit) to uint (32-bit)
    public void TestLessThan(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax>bx", "AX>BX")]
    [InlineData("ax>0x100", "(uint)AX>0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("0x100>ax", "0x00000100>(uint)AX")]  // Type conversion: AX (16-bit) to uint (32-bit)
    public void TestGreaterThan(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax<=bx", "AX<=BX")]
    [InlineData("ax<=0x100", "(uint)AX<=0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("0x100<=ax", "0x00000100<=(uint)AX")]  // Type conversion: AX (16-bit) to uint (32-bit)
    public void TestLessThanOrEqual(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax>=bx", "AX>=BX")]
    [InlineData("ax>=0x100", "(uint)AX>=0x00000100")]  // Type conversion: AX (16-bit) to uint (32-bit)
    [InlineData("0x100>=ax", "0x00000100>=(uint)AX")]  // Type conversion: AX (16-bit) to uint (32-bit)
    public void TestGreaterThanOrEqual(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax==0x100&&bx==0x200", "(uint)AX==0x00000100&&(uint)BX==0x00000200")]
    [InlineData("al==0x42&&ah==0x10", "(uint)AL==0x00000042&&(uint)AH==0x00000010")]
    [InlineData("ax>0&&bx<0x100", "(ushort)AX>0&&(uint)BX<0x00000100")]
    public void TestLogicalAnd(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax==0x100||bx==0x200", "(uint)AX==0x00000100||(uint)BX==0x00000200")]
    [InlineData("al==0x42||ah==0x10", "(uint)AL==0x00000042||(uint)AH==0x00000010")]
    [InlineData("ax>0||bx<0x100", "(ushort)AX>0||(uint)BX<0x00000100")]
    public void TestLogicalOr(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax&bx", "AX&BX")]
    [InlineData("ax&0xFF", "AX&0x000000FF")]
    [InlineData("0xFF00&ax", "0x0000FF00&AX")]
    public void TestBitwiseAnd(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax|bx", "AX|BX")]
    [InlineData("ax|0xFF", "AX|0x000000FF")]
    [InlineData("0xFF00|ax", "0x0000FF00|AX")]
    public void TestBitwiseOr(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax^bx", "AX^BX")]
    [InlineData("ax^0xFF", "AX^0x000000FF")]
    [InlineData("0xFF00^ax", "0x0000FF00^AX")]
    public void TestBitwiseXor(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax<<1", "AX<<1")]
    [InlineData("ax<<bx", "AX<<BX")]
    [InlineData("0x100<<2", "0x00000100<<2")]
    public void TestLeftShift(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax>>1", "AX>>1")]
    [InlineData("ax>>bx", "AX>>BX")]
    [InlineData("0x100>>2", "0x00000100>>2")]
    public void TestRightShift(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("!ax", "!AX")]
    [InlineData("!(ax==0x100)", "!((uint)AX==0x00000100)")]  // Type conversion for comparison is added as expected
    public void TestLogicalNot(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("-ax", "-AX")]
    [InlineData("-1")]
    [InlineData("-(ax+bx)", "-(AX+BX)")]
    public void TestNegate(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("~ax", "~AX")]
    [InlineData("~0xFF", "~0x000000FF")]
    [InlineData("~(ax&bx)", "~(AX&BX)")]
    public void TestBitwiseNot(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("byte ptr [0x1000]", "byte ptr [0x00001000]")]
    [InlineData("word ptr [0x1000]", "word ptr [0x00001000]")]
    [InlineData("dword ptr [0x1000]", "dword ptr [0x00001000]")]
    [InlineData("byte ptr [ax]", "byte ptr [AX]")]
    [InlineData("word ptr [bx]", "word ptr [BX]")]
    [InlineData("dword ptr [ebx]", "dword ptr [EBX]")]
    [InlineData("byte ptr [ax+bx]", "byte ptr [AX+BX]")]
    [InlineData("word ptr [0x1000+ax]", "word ptr [0x00001000+AX]")]
    public void TestAbsolutePointer(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("byte ptr es:[0x100]", "byte ptr ES:[0x00000100]")]
    [InlineData("word ptr ds:[0x100]", "word ptr DS:[0x00000100]")]
    [InlineData("dword ptr fs:[0x100]", "dword ptr FS:[0x00000100]")]
    [InlineData("byte ptr es:[bx]", "byte ptr ES:[BX]")]
    [InlineData("word ptr ds:[si]", "word ptr DS:[SI]")]
    [InlineData("dword ptr fs:[edi]", "dword ptr FS:[EDI]")]
    [InlineData("byte ptr es:[bx+si]", "byte ptr ES:[BX+SI]")]
    [InlineData("word ptr ds:[0x100+ax]", "word ptr DS:[0x00000100+AX]")]
    public void TestSegmentedPointer(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("(ax)", "AX")]  // Parentheses not preserved when redundant
    [InlineData("(ax+bx)", "AX+BX")]  // Parentheses not preserved when redundant  
    [InlineData("((ax+bx)*2)", "(AX+BX)*2")]  // Parentheses preserved for correct precedence
    [InlineData("(ax==0x100)", "(uint)AX==0x00000100")]  // Outer parentheses not preserved
    public void TestParentheses(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax+bx*cx", "AX+BX*CX")]
    [InlineData("ax*bx+cx", "AX*BX+CX")]
    [InlineData("(ax+bx)*cx", "(AX+BX)*CX")]  // Parentheses preserved for correct precedence
    [InlineData("ax+(bx*cx)", "AX+BX*CX")]  // Redundant parentheses removed
    public void TestOperatorPrecedence(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax==0x100&&bx==0x200||cx==0x300", "(uint)AX==0x00000100&&(uint)BX==0x00000200||(uint)CX==0x00000300")]
    [InlineData("ax==0x100||(bx==0x200&&cx==0x300)", "(uint)AX==0x00000100||(uint)BX==0x00000200&&(uint)CX==0x00000300")]  // Redundant parentheses removed
    [InlineData("(ax==0x100||bx==0x200)&&cx==0x300", "((uint)AX==0x00000100||(uint)BX==0x00000200)&&(uint)CX==0x00000300")]
    [InlineData("ax+bx>0x100&&cx<0x200", "AX+BX>0x00000100&&(uint)CX<0x00000200")]  // Addition result not wrapped in type conversion
    [InlineData("byte ptr [ax]==0x42&&bx!=0", "(uint)byte ptr [AX]==0x00000042&&(ushort)BX!=0")]  // Address not type converted
    [InlineData("word ptr ds:[bx]>0x100||ax==0", "(uint)word ptr DS:[BX]>0x00000100||(ushort)AX==0")]  // Address not type converted
    public void TestComplexExpressions(string expression, string? expected = null) {
        AssertRoundTrip(expression, expected);
    }

    [Theory]
    [InlineData("ax & bx", "AX&BX")]
    [InlineData("ax | bx", "AX|BX")]
    [InlineData("ax + bx", "AX+BX")]
    [InlineData("ax - bx", "AX-BX")]
    [InlineData("ax * bx", "AX*BX")]
    [InlineData("ax / bx", "AX/BX")]
    [InlineData("ax % bx", "AX%BX")]
    [InlineData("ax == bx", "AX==BX")]
    [InlineData("ax != bx", "AX!=BX")]
    [InlineData("ax < bx", "AX<BX")]
    [InlineData("ax > bx", "AX>BX")]
    [InlineData("ax <= bx", "AX<=BX")]
    [InlineData("ax >= bx", "AX>=BX")]
    [InlineData("ax && bx", "AX&&BX")]
    [InlineData("ax || bx", "AX||BX")]
    public void TestWhitespaceNormalization(string input, string expected) {
        AssertRoundTrip(input, expected);
    }

    [Theory]
    [InlineData("AX", "AX")]
    [InlineData("BX", "BX")]
    [InlineData("ES", "ES")]
    [InlineData("DS", "DS")]
    [InlineData("BYTE PTR [0x100]", "byte ptr [0x00000100]")]
    [InlineData("WORD PTR DS:[BX]", "word ptr DS:[BX]")]
    public void TestCaseInsensitivity(string input, string expected) {
        AssertRoundTrip(input, expected);
    }

    [Theory]
    [InlineData("0x0", "0")]
    [InlineData("0x1", "1")]
    [InlineData("0x2", "2")]
    [InlineData("0x3", "3")]
    [InlineData("0x4", "4")]
    [InlineData("0x5", "5")]
    [InlineData("0x6", "6")]
    [InlineData("0x7", "7")]
    [InlineData("0x8", "8")]
    [InlineData("0x9", "9")]
    public void TestSingleDigitHexRendersAsDecimal(string input, string expected) {
        AssertRoundTrip(input, expected);
    }

    [Fact]
    public void TestNestedLogicalAndOr() {
        AssertRoundTrip("(ax==0x100||bx==0x200)&&(cx==0x300||dx==0x400)", 
            "((uint)AX==0x00000100||(uint)BX==0x00000200)&&((uint)CX==0x00000300||(uint)DX==0x00000400)");
    }

    [Fact]
    public void TestNestedBitwiseOperations() {
        // Parentheses are redundant since & has higher precedence than |
        AssertRoundTrip("(ax&0xFF)|(bx&0xFF00)", "AX&0x000000FF|BX&0x0000FF00");
    }

    [Fact]
    public void TestComplexMemoryExpression() {
        // Address expression is not type-converted
        AssertRoundTrip("byte ptr [ax+bx*2]==0x42", "(uint)byte ptr [AX+BX*2]==0x00000042");
    }

    [Fact]
    public void TestMultipleUnaryOperators() {
        AssertRoundTrip("!!ax", "!!AX");
    }

    [Fact]
    public void TestNegatedComparison() {
        AssertRoundTrip("!(ax==0x100)", "!((uint)AX==0x00000100)");
    }

    [Fact]
    public void TestBitwiseNotWithAnd() {
        AssertRoundTrip("~ax&0xFF", "~AX&0x000000FF");
    }
}




