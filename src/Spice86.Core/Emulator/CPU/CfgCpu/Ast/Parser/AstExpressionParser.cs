namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Shared.Emulator.Memory;

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
                ValueNode right = ParseRelational();
                left = ApplyBinaryOperation(BinaryOperation.EQUAL, left, right);
            } else if (Match("!=")) {
                ValueNode right = ParseRelational();
                left = ApplyBinaryOperation(BinaryOperation.NOT_EQUAL, left, right);
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
                ValueNode right = ParseShift();
                left = ApplyBinaryOperation(BinaryOperation.LESS_THAN_OR_EQUAL, left, right);
            } else if (Match(">=")) {
                ValueNode right = ParseShift();
                left = ApplyBinaryOperation(BinaryOperation.GREATER_THAN_OR_EQUAL, left, right);
            } else if (CurrentChar() == '<') {
                Advance();
                ValueNode right = ParseShift();
                left = ApplyBinaryOperation(BinaryOperation.LESS_THAN, left, right);
            } else if (CurrentChar() == '>') {
                Advance();
                ValueNode right = ParseShift();
                left = ApplyBinaryOperation(BinaryOperation.GREATER_THAN, left, right);
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
            } else if (CurrentChar() == '-') {
                // Subtraction: negative numbers are handled separately in ParseUnary
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
                throw new ExpressionParseException("Expected ')'", _input, _position);
            }
            Advance();
            return expr;
        }

        // Memory access: byte ptr [...], word ptr [...], dword ptr [...], or byte ptr segment:[...]
        if (Match("byte")) {
            SkipWhitespace();
            if (Match("ptr")) {
                SkipWhitespace();
                return ParsePointerNode(DataType.UINT8);
            }
        }
        if (Match("word")) {
            SkipWhitespace();
            if (Match("ptr")) {
                SkipWhitespace();
                return ParsePointerNode(DataType.UINT16);
            }
        }
        if (Match("dword")) {
            SkipWhitespace();
            if (Match("ptr")) {
                SkipWhitespace();
                return ParsePointerNode(DataType.UINT32);
            }
        }

        // Numbers
        if (char.IsDigit(CurrentChar()) || (CurrentChar() == '0' && (PeekChar() == 'x' || PeekChar() == 'X'))) {
            return ParseNumber();
        }

        // Identifiers (registers, keywords)
        if (char.IsLetter(CurrentChar()) || CurrentChar() == '_') {
            return ParseIdentifier();
        }

        throw new ExpressionParseException($"Unexpected character: '{CurrentChar()}'", _input, _position);
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
        while (_position < _input.Length && (char.IsLetterOrDigit(_input[_position]) || _input[_position] == '_')) {
            _position++;
        }
        string identifier = _input.Substring(start, _position - start).ToLowerInvariant();

        return identifier switch {
            // 8-bit registers (low)
            "al" => new RegisterNode(DataType.UINT8, 0),
            "cl" => new RegisterNode(DataType.UINT8, 1),
            "dl" => new RegisterNode(DataType.UINT8, 2),
            "bl" => new RegisterNode(DataType.UINT8, 3),
            // 8-bit registers (high)
            "ah" => new RegisterNode(DataType.UINT8, 4),
            "ch" => new RegisterNode(DataType.UINT8, 5),
            "dh" => new RegisterNode(DataType.UINT8, 6),
            "bh" => new RegisterNode(DataType.UINT8, 7),
            // 16-bit registers
            "ax" => new RegisterNode(DataType.UINT16, 0),
            "cx" => new RegisterNode(DataType.UINT16, 1),
            "dx" => new RegisterNode(DataType.UINT16, 2),
            "bx" => new RegisterNode(DataType.UINT16, 3),
            "sp" => new RegisterNode(DataType.UINT16, 4),
            "bp" => new RegisterNode(DataType.UINT16, 5),
            "si" => new RegisterNode(DataType.UINT16, 6),
            "di" => new RegisterNode(DataType.UINT16, 7),
            // 32-bit registers
            "eax" => new RegisterNode(DataType.UINT32, 0),
            "ecx" => new RegisterNode(DataType.UINT32, 1),
            "edx" => new RegisterNode(DataType.UINT32, 2),
            "ebx" => new RegisterNode(DataType.UINT32, 3),
            "esp" => new RegisterNode(DataType.UINT32, 4),
            "ebp" => new RegisterNode(DataType.UINT32, 5),
            "esi" => new RegisterNode(DataType.UINT32, 6),
            "edi" => new RegisterNode(DataType.UINT32, 7),
            // Segment registers
            "es" => new SegmentRegisterNode(0),
            "cs" => new SegmentRegisterNode(1),
            "ss" => new SegmentRegisterNode(2),
            "ds" => new SegmentRegisterNode(3),
            "fs" => new SegmentRegisterNode(4),
            "gs" => new SegmentRegisterNode(5),
            _ => throw new ExpressionParseException($"Unknown identifier: '{identifier}'", _input, _position - identifier.Length)
        };
    }

    private ValueNode ParsePointerNode(DataType dataType) {
        // Parse either absolute pointer [addr] or segmented pointer segment:[offset]
        SkipWhitespace();

        // Check if it's a segmented register (like ES, CS, DS, etc.)
        int savedPosition = _position;
        if (char.IsLetter(CurrentChar())) {
            ValueNode potentialSegment = ParseIdentifier();
            SkipWhitespace();
            if (CurrentChar() == ':') {
                // It's a segmented pointer: segment:[offset]
                Advance(); // skip ':'
                if (CurrentChar() != '[') {
                    throw new ExpressionParseException("Expected '[' after segment:", _input, _position);
                }
                Advance(); // skip '['
                ValueNode offset = ParseExpression();
                if (CurrentChar() != ']') {
                    throw new ExpressionParseException("Expected ']'", _input, _position);
                }
                Advance();
                return new SegmentedPointerNode(dataType, potentialSegment, offset);
            }
            // Not a segmented pointer, restore position
            _position = savedPosition;
        }

        // Absolute pointer: [address]
        if (CurrentChar() != '[') {
            throw new ExpressionParseException("Expected '[' or segment register for pointer", _input, _position);
        }
        Advance(); // skip '['
        ValueNode address = ParseExpression();
        if (CurrentChar() != ']') {
            throw new ExpressionParseException("Expected ']'", _input, _position);
        }
        Advance();
        return new AbsolutePointerNode(dataType, address);
    }

    private ValueNode EnsureTypeCompatibility(ValueNode left, ValueNode right, out DataType commonType) {
        // Determine the common type based on operand types
        DataType leftType = left.DataType;
        DataType rightType = right.DataType;

        // If types are the same, no conversion needed
        if (leftType.BitWidth == rightType.BitWidth && leftType.Signed == rightType.Signed) {
            commonType = leftType;
            return left;
        }

        // When comparing to constant 0, use the non-constant operand's type
        if (right is ConstantNode { Value: 0 }) {
            commonType = leftType;
            // Add explicit type annotation for clarity
            left = new TypeConversionNode(leftType, left);
            return left;
        }

        // Choose the larger type
        commonType = GetLargerType(leftType, rightType);

        // Convert left if needed
        if (leftType.BitWidth != commonType.BitWidth || leftType.Signed != commonType.Signed) {
            left = new TypeConversionNode(commonType, left);
        }

        return left;
    }

    private DataType GetLargerType(DataType type1, DataType type2) {
        // Order by bit width: 8 < 16 < 32
        if (type1.BitWidth != type2.BitWidth) {
            return (int)type1.BitWidth > (int)type2.BitWidth ? type1 : type2;
        }
        // Same width: prefer unsigned
        return type1.Signed ? type2 : type1;
    }

    private ValueNode ApplyBinaryOperation(BinaryOperation operation, ValueNode left, ValueNode right) {
        // For comparison and logical operations, convert operands to common type first
        if (operation == BinaryOperation.EQUAL || operation == BinaryOperation.NOT_EQUAL ||
            operation == BinaryOperation.LESS_THAN || operation == BinaryOperation.GREATER_THAN ||
            operation == BinaryOperation.LESS_THAN_OR_EQUAL || operation == BinaryOperation.GREATER_THAN_OR_EQUAL) {
            // Ensure operands have compatible types
            DataType commonType;
            left = EnsureTypeCompatibility(left, right, out commonType);
            // Don't convert constant 0 - it implicitly matches any type
            if (!(right is ConstantNode { Value: 0 }) &&
                (right.DataType.BitWidth != commonType.BitWidth || right.DataType.Signed != commonType.Signed)) {
                right = new TypeConversionNode(commonType, right);
            }
            return new BinaryOperationNode(DataType.BOOL, left, operation, right);
        }

        // For arithmetic and bitwise operations, use UINT32 as default result type
        return new BinaryOperationNode(DataType.UINT32, left, operation, right);
    }

    private bool Match(string text) {
        SkipWhitespace();
        if (_position + text.Length <= _input.Length &&
            string.Equals(_input.Substring(_position, text.Length), text, StringComparison.OrdinalIgnoreCase)) {
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