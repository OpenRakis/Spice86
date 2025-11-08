namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Shared.Emulator.VM.Breakpoint.Expression;

using Xunit;

/// <summary>
/// Tests for the expression parser and evaluator for conditional breakpoints.
/// </summary>
public class ExpressionParserTests {
    private class TestContext : IExpressionContext {
        private readonly Dictionary<string, long> _variables = new();
        private readonly Dictionary<long, byte> _memory = new();
        
        public void SetVariable(string name, long value) {
            _variables[name] = value;
        }
        
        public void SetMemoryByte(long address, byte value) {
            _memory[address] = value;
        }
        
        public long GetVariable(string variableName) {
            return _variables.TryGetValue(variableName, out long value) ? value : 0;
        }
        
        public byte ReadMemoryByte(long address) {
            return _memory.TryGetValue(address, out byte value) ? value : (byte)0;
        }
        
        public ushort ReadMemoryWord(long address) {
            byte low = ReadMemoryByte(address);
            byte high = ReadMemoryByte(address + 1);
            return (ushort)((high << 8) | low);
        }
        
        public uint ReadMemoryDword(long address) {
            ushort low = ReadMemoryWord(address);
            ushort high = ReadMemoryWord(address + 2);
            return (uint)((high << 16) | low);
        }
    }
    
    [Fact]
    public void TestConstantExpression() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        
        var expr = parser.Parse("42");
        expr.Evaluate(context).Should().Be(42);
        
        expr = parser.Parse("0x10");
        expr.Evaluate(context).Should().Be(16);
    }
    
    [Fact]
    public void TestVariableExpression() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("ax", 100);
        
        var expr = parser.Parse("ax");
        expr.Evaluate(context).Should().Be(100);
    }
    
    [Fact]
    public void TestArithmeticOperations() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("x", 10);
        context.SetVariable("y", 3);
        
        parser.Parse("x + y").Evaluate(context).Should().Be(13);
        parser.Parse("x - y").Evaluate(context).Should().Be(7);
        parser.Parse("x * y").Evaluate(context).Should().Be(30);
        parser.Parse("x / y").Evaluate(context).Should().Be(3);
        parser.Parse("x % y").Evaluate(context).Should().Be(1);
    }
    
    [Fact]
    public void TestComparisonOperations() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("x", 10);
        context.SetVariable("y", 5);
        
        parser.Parse("x == 10").Evaluate(context).Should().Be(1);
        parser.Parse("x == 5").Evaluate(context).Should().Be(0);
        parser.Parse("x != 5").Evaluate(context).Should().Be(1);
        parser.Parse("x > y").Evaluate(context).Should().Be(1);
        parser.Parse("x < y").Evaluate(context).Should().Be(0);
        parser.Parse("x >= 10").Evaluate(context).Should().Be(1);
        parser.Parse("y <= 5").Evaluate(context).Should().Be(1);
    }
    
    [Fact]
    public void TestLogicalOperations() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("x", 1);
        context.SetVariable("y", 0);
        
        parser.Parse("x && y").Evaluate(context).Should().Be(0);
        parser.Parse("x || y").Evaluate(context).Should().Be(1);
        parser.Parse("!x").Evaluate(context).Should().Be(0);
        parser.Parse("!y").Evaluate(context).Should().Be(1);
    }
    
    [Fact]
    public void TestBitwiseOperations() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("x", 0b1100);
        context.SetVariable("y", 0b1010);
        
        parser.Parse("x & y").Evaluate(context).Should().Be(0b1000);
        parser.Parse("x | y").Evaluate(context).Should().Be(0b1110);
        parser.Parse("x ^ y").Evaluate(context).Should().Be(0b0110);
        parser.Parse("~x").Evaluate(context).Should().Be(~0b1100L);
        parser.Parse("x << 2").Evaluate(context).Should().Be(0b110000);
        parser.Parse("x >> 2").Evaluate(context).Should().Be(0b0011);
    }
    
    [Fact]
    public void TestUnaryOperations() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("x", 5);
        
        parser.Parse("-x").Evaluate(context).Should().Be(-5);
        parser.Parse("!x").Evaluate(context).Should().Be(0);
        parser.Parse("!(x == 0)").Evaluate(context).Should().Be(1);
    }
    
    [Fact]
    public void TestMemoryAccess() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetMemoryByte(0x100, 0x42);
        context.SetMemoryByte(0x101, 0x43);
        context.SetMemoryByte(0x102, 0x44);
        context.SetMemoryByte(0x103, 0x45);
        
        parser.Parse("byte[0x100]").Evaluate(context).Should().Be(0x42);
        parser.Parse("word[0x100]").Evaluate(context).Should().Be(0x4342);
        parser.Parse("dword[0x100]").Evaluate(context).Should().Be(0x45444342);
    }
    
    [Fact]
    public void TestComplexExpression() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("ax", 10);
        context.SetVariable("bx", 20);
        context.SetMemoryByte(0x100, 5);
        
        var expr = parser.Parse("(ax + bx) * 2 == 60");
        expr.Evaluate(context).Should().Be(1);
        
        expr = parser.Parse("byte[0x100] > 3 && ax < 15");
        expr.Evaluate(context).Should().Be(1);
    }
    
    [Fact]
    public void TestOperatorPrecedence() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        
        parser.Parse("2 + 3 * 4").Evaluate(context).Should().Be(14);
        parser.Parse("(2 + 3) * 4").Evaluate(context).Should().Be(20);
        parser.Parse("1 + 2 * 3 + 4").Evaluate(context).Should().Be(11);
    }
    
    [Fact]
    public void TestExpressionSerialization() {
        var parser = new ExpressionParser();
        var context = new TestContext();
        context.SetVariable("ax", 100);
        
        var expr = parser.Parse("ax == 100");
        var str = expr.ToString();
        
        // Parse the serialized expression and verify it evaluates the same
        var expr2 = parser.Parse(str);
        expr2.Evaluate(context).Should().Be(expr.Evaluate(context));
    }
    
    [Fact]
    public void TestInvalidExpression() {
        var parser = new ExpressionParser();
        
        Action act = () => parser.Parse("");
        act.Should().Throw<ArgumentException>();
        
        act = () => parser.Parse("   ");
        act.Should().Throw<ArgumentException>();
        
        act = () => parser.Parse("(ax + 5");
        act.Should().Throw<ArgumentException>();
    }
}
