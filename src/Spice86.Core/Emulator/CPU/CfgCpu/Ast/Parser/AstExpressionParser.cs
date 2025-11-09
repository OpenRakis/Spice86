namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using System.Globalization;

/// <summary>
/// Parses breakpoint condition strings into CfgCpu AST nodes.
/// Supports C-like expression syntax with operators, registers, and memory access.
/// </summary>
public class AstExpressionParser {
    private string _input = string.Empty;
    private int _position;

    /// <summary>
    /// Parses a condition expression string into an AST.
    /// </summary>
    /// <param name="expression">The expression string to parse.</param>
    /// <returns>A ValueNode representing the parsed expression.</returns>
    public ValueNode Parse(string expression) {
        _input = expression;
        _position = 0;
        return ParseExpression();
    }

    private ValueNode ParseExpression() {
        return ParseLogicalOr();
    }

    private ValueNode ParseLogicalOr() {
        ValueNode left = ParseLogicalAnd();
        while (Match("||")) {
            left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LOGICAL_OR, ParseLogicalAnd());
        }
        return left;
    }

    private ValueNode ParseLogicalAnd() {
        ValueNode left = ParseBitwiseOr();
        while (Match("&&")) {
            left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LOGICAL_AND, ParseBitwiseOr());
        }
        return left;
    }

    private ValueNode ParseBitwiseOr() {
        ValueNode left = ParseBitwiseXor();
        while (CurrentChar() == '|' && PeekChar() != '|') {
            Advance();
            left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.BITWISE_OR, ParseBitwiseXor());
        }
        return left;
    }

    private ValueNode ParseBitwiseXor() {
        ValueNode left = ParseBitwiseAnd();
        while (CurrentChar() == '^') {
            Advance();
            left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.BITWISE_XOR, ParseBitwiseAnd());
        }
        return left;
    }

    private ValueNode ParseBitwiseAnd() {
        ValueNode left = ParseEquality();
        while (CurrentChar() == '&' && PeekChar() != '&') {
            Advance();
            left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.BITWISE_AND, ParseEquality());
        }
        return left;
    }

    private ValueNode ParseEquality() {
        ValueNode left = ParseRelational();
        while (true) {
            if (Match("==")) {
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.EQUAL, ParseRelational());
            } else if (Match("!=")) {
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.NOT_EQUAL, ParseRelational());
            } else {
                break;
            }
        }
        return left;
    }

    private ValueNode ParseRelational() {
        ValueNode left = ParseShift();
        while (true) {
            if (Match("<=")) {
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LESS_THAN_OR_EQUAL, ParseShift());
            } else if (Match(">=")) {
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.GREATER_THAN_OR_EQUAL, ParseShift());
            } else if (CurrentChar() == '<') {
                Advance();
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LESS_THAN, ParseShift());
            } else if (CurrentChar() == '>') {
                Advance();
                left = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.GREATER_THAN, ParseShift());
            } else {
                break;
            }
        }
        return left;
    }

    private ValueNode ParseShift() {
        ValueNode left = ParseAdditive();
        while (true) {
            if (Match("<<")) {
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.LEFT_SHIFT, ParseAdditive());
            } else if (Match(">>")) {
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.RIGHT_SHIFT, ParseAdditive());
            } else {
                break;
            }
        }
        return left;
    }

    private ValueNode ParseAdditive() {
        ValueNode left = ParseMultiplicative();
        while (true) {
            if (CurrentChar() == '+') {
                Advance();
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.PLUS, ParseMultiplicative());
            } else if (CurrentChar() == '-' && !char.IsDigit(PeekChar())) {
                Advance();
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.MINUS, ParseMultiplicative());
            } else {
                break;
            }
        }
        return left;
    }

    private ValueNode ParseMultiplicative() {
        ValueNode left = ParseUnary();
        while (true) {
            if (CurrentChar() == '*') {
                Advance();
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.MULTIPLY, ParseUnary());
            } else if (CurrentChar() == '/') {
                Advance();
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.DIVIDE, ParseUnary());
            } else if (CurrentChar() == '%') {
                Advance();
                left = new BinaryOperationNode(DataType.UINT32, left, BinaryOperation.MODULO, ParseUnary());
            } else {
                break;
            }
        }
        return left;
    }

    private ValueNode ParseUnary() {
        if (CurrentChar() == '!') {
            Advance();
            return new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, ParseUnary());
        }
        if (CurrentChar() == '-') {
            Advance();
            return new UnaryOperationNode(DataType.UINT32, UnaryOperation.NEGATE, ParseUnary());
        }
        if (CurrentChar() == '~') {
            Advance();
            return new UnaryOperationNode(DataType.UINT32, UnaryOperation.BITWISE_NOT, ParseUnary());
        }
        return ParsePrimary();
    }

    private ValueNode ParsePrimary() {
        SkipWhitespace();

        // Parentheses
        if (CurrentChar() == '(') {
            Advance();
            ValueNode expr = ParseExpression();
            if (CurrentChar() != ')') {
                throw new ArgumentException("Expected ')'");
            }
            Advance();
            return expr;
        }

        // Memory access: byte[...], word[...], dword[...]
        if (Match("byte[")) {
            ValueNode address = ParseExpression();
            if (CurrentChar() != ']') {
                throw new ArgumentException("Expected ']'");
            }
            Advance();
            return new AbsolutePointerNode(DataType.UINT8, address);
        }
        if (Match("word[")) {
            ValueNode address = ParseExpression();
            if (CurrentChar() != ']') {
                throw new ArgumentException("Expected ']'");
            }
            Advance();
            return new AbsolutePointerNode(DataType.UINT16, address);
        }
        if (Match("dword[")) {
            ValueNode address = ParseExpression();
            if (CurrentChar() != ']') {
                throw new ArgumentException("Expected ']'");
            }
            Advance();
            return new AbsolutePointerNode(DataType.UINT32, address);
        }

        // Numbers
        if (char.IsDigit(CurrentChar()) || (CurrentChar() == '0' && (PeekChar() == 'x' || PeekChar() == 'X'))) {
            return ParseNumber();
        }

        // Identifiers (registers, keywords)
        if (char.IsLetter(CurrentChar()) || CurrentChar() == '_') {
            return ParseIdentifier();
        }

        throw new ArgumentException($"Unexpected character: {CurrentChar()} at position {_position}");
    }

    private ValueNode ParseNumber() {
        int start = _position;
        
        // Hexadecimal
        if (CurrentChar() == '0' && (PeekChar() == 'x' || PeekChar() == 'X')) {
            Advance(); // skip '0'
            Advance(); // skip 'x'
            while (char.IsAsciiHexDigit(CurrentChar())) {
                Advance();
            }
            string hexStr = _input.Substring(start + 2, _position - start - 2);
            uint value = uint.Parse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new ConstantNode(DataType.UINT32, value);
        }

        // Decimal
        while (char.IsDigit(CurrentChar())) {
            Advance();
        }
        string numStr = _input.Substring(start, _position - start);
        uint decValue = uint.Parse(numStr, CultureInfo.InvariantCulture);
        return new ConstantNode(DataType.UINT32, decValue);
    }

    private ValueNode ParseIdentifier() {
        int start = _position;
        while (char.IsLetterOrDigit(CurrentChar()) || CurrentChar() == '_') {
            Advance();
        }
        string identifier = _input.Substring(start, _position - start).ToLowerInvariant();

        // Special keyword: address (the trigger address)
        if (identifier == "address") {
            // Return a constant representing the trigger address (will be set at evaluation time)
            // For now, use 0 as a placeholder; the evaluator will handle this specially
            return new ConstantNode(DataType.UINT32, 0); // Special marker for trigger address
        }

        // 16-bit registers
        return identifier switch {
            "ax" => new RegisterNode(DataType.UINT16, 0),
            "cx" => new RegisterNode(DataType.UINT16, 1),
            "dx" => new RegisterNode(DataType.UINT16, 2),
            "bx" => new RegisterNode(DataType.UINT16, 3),
            "sp" => new RegisterNode(DataType.UINT16, 4),
            "bp" => new RegisterNode(DataType.UINT16, 5),
            "si" => new RegisterNode(DataType.UINT16, 6),
            "di" => new RegisterNode(DataType.UINT16, 7),
            // Segment registers
            "es" => new SegmentRegisterNode(0),
            "cs" => new SegmentRegisterNode(1),
            "ss" => new SegmentRegisterNode(2),
            "ds" => new SegmentRegisterNode(3),
            "fs" => new SegmentRegisterNode(4),
            "gs" => new SegmentRegisterNode(5),
            _ => throw new ArgumentException($"Unknown identifier: {identifier}")
        };
    }

    private bool Match(string text) {
        SkipWhitespace();
        if (_position + text.Length <= _input.Length &&
            _input.Substring(_position, text.Length) == text) {
            _position += text.Length;
            return true;
        }
        return false;
    }

    private char CurrentChar() {
        SkipWhitespace();
        return _position < _input.Length ? _input[_position] : '\0';
    }

    private char PeekChar() {
        return _position + 1 < _input.Length ? _input[_position + 1] : '\0';
    }

    private void Advance() {
        if (_position < _input.Length) {
            _position++;
        }
    }

    private void SkipWhitespace() {
        while (_position < _input.Length && char.IsWhiteSpace(_input[_position])) {
            _position++;
        }
    }
}
