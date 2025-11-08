namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

using System.Text.RegularExpressions;

/// <summary>
/// Parser for converting string expressions to AST nodes.
/// </summary>
public partial class ExpressionParser {
    private string _expression = string.Empty;
    private int _position;
    
    /// <summary>
    /// Parses a string expression into an AST.
    /// </summary>
    /// <param name="expression">The expression string to parse.</param>
    /// <returns>The root node of the AST.</returns>
    /// <exception cref="ArgumentException">Thrown when the expression is invalid.</exception>
    public IExpressionNode Parse(string expression) {
        if (string.IsNullOrWhiteSpace(expression)) {
            throw new ArgumentException("Expression cannot be null or empty", nameof(expression));
        }
        
        _expression = expression;
        _position = 0;
        
        IExpressionNode result = ParseOrExpression();
        
        SkipWhitespace();
        if (_position < _expression.Length) {
            throw new ArgumentException($"Unexpected character at position {_position}: '{_expression[_position]}'");
        }
        
        return result;
    }
    
    private IExpressionNode ParseOrExpression() {
        IExpressionNode left = ParseAndExpression();
        
        while (true) {
            SkipWhitespace();
            if (Match("||")) {
                IExpressionNode right = ParseAndExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Or, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseAndExpression() {
        IExpressionNode left = ParseBitwiseOrExpression();
        
        while (true) {
            SkipWhitespace();
            if (Match("&&")) {
                IExpressionNode right = ParseBitwiseOrExpression();
                left = new BinaryOperationNode(left, BinaryOperator.And, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseBitwiseOrExpression() {
        IExpressionNode left = ParseBitwiseXorExpression();
        
        while (true) {
            SkipWhitespace();
            if (PeekChar() == '|' && !PeekString("||")) {
                Consume();
                IExpressionNode right = ParseBitwiseXorExpression();
                left = new BinaryOperationNode(left, BinaryOperator.BitwiseOr, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseBitwiseXorExpression() {
        IExpressionNode left = ParseBitwiseAndExpression();
        
        while (true) {
            SkipWhitespace();
            if (PeekChar() == '^') {
                Consume();
                IExpressionNode right = ParseBitwiseAndExpression();
                left = new BinaryOperationNode(left, BinaryOperator.BitwiseXor, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseBitwiseAndExpression() {
        IExpressionNode left = ParseComparisonExpression();
        
        while (true) {
            SkipWhitespace();
            if (PeekChar() == '&' && !PeekString("&&")) {
                Consume();
                IExpressionNode right = ParseComparisonExpression();
                left = new BinaryOperationNode(left, BinaryOperator.BitwiseAnd, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseComparisonExpression() {
        IExpressionNode left = ParseShiftExpression();
        
        while (true) {
            SkipWhitespace();
            if (Match("==")) {
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Equal, right);
            } else if (Match("!=")) {
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.NotEqual, right);
            } else if (Match("<=")) {
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.LessThanOrEqual, right);
            } else if (Match(">=")) {
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.GreaterThanOrEqual, right);
            } else if (PeekChar() == '<' && !PeekString("<<")) {
                Consume();
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.LessThan, right);
            } else if (PeekChar() == '>' && !PeekString(">>")) {
                Consume();
                IExpressionNode right = ParseShiftExpression();
                left = new BinaryOperationNode(left, BinaryOperator.GreaterThan, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseShiftExpression() {
        IExpressionNode left = ParseAdditiveExpression();
        
        while (true) {
            SkipWhitespace();
            if (Match("<<")) {
                IExpressionNode right = ParseAdditiveExpression();
                left = new BinaryOperationNode(left, BinaryOperator.LeftShift, right);
            } else if (Match(">>")) {
                IExpressionNode right = ParseAdditiveExpression();
                left = new BinaryOperationNode(left, BinaryOperator.RightShift, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseAdditiveExpression() {
        IExpressionNode left = ParseMultiplicativeExpression();
        
        while (true) {
            SkipWhitespace();
            if (PeekChar() == '+') {
                Consume();
                IExpressionNode right = ParseMultiplicativeExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Add, right);
            } else if (PeekChar() == '-' && !IsStartOfNumber()) {
                Consume();
                IExpressionNode right = ParseMultiplicativeExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Subtract, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseMultiplicativeExpression() {
        IExpressionNode left = ParseUnaryExpression();
        
        while (true) {
            SkipWhitespace();
            if (PeekChar() == '*') {
                Consume();
                IExpressionNode right = ParseUnaryExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Multiply, right);
            } else if (PeekChar() == '/') {
                Consume();
                IExpressionNode right = ParseUnaryExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Divide, right);
            } else if (PeekChar() == '%') {
                Consume();
                IExpressionNode right = ParseUnaryExpression();
                left = new BinaryOperationNode(left, BinaryOperator.Modulo, right);
            } else {
                break;
            }
        }
        
        return left;
    }
    
    private IExpressionNode ParseUnaryExpression() {
        SkipWhitespace();
        
        if (PeekChar() == '-') {
            Consume();
            return new UnaryOperationNode(UnaryOperator.Negate, ParseUnaryExpression());
        } else if (PeekChar() == '!') {
            Consume();
            return new UnaryOperationNode(UnaryOperator.Not, ParseUnaryExpression());
        } else if (PeekChar() == '~') {
            Consume();
            return new UnaryOperationNode(UnaryOperator.BitwiseNot, ParseUnaryExpression());
        }
        
        return ParsePrimaryExpression();
    }
    
    private IExpressionNode ParsePrimaryExpression() {
        SkipWhitespace();
        
        // Parenthesized expression
        if (PeekChar() == '(') {
            Consume();
            IExpressionNode expr = ParseOrExpression();
            SkipWhitespace();
            if (PeekChar() != ')') {
                throw new ArgumentException($"Expected ')' at position {_position}");
            }
            Consume();
            return expr;
        }
        
        // Memory access: byte[expr], word[expr], dword[expr]
        if (Match("byte[")) {
            IExpressionNode addr = ParseOrExpression();
            SkipWhitespace();
            if (PeekChar() != ']') {
                throw new ArgumentException($"Expected ']' at position {_position}");
            }
            Consume();
            return new MemoryAccessNode(addr, MemoryAccessSize.Byte);
        } else if (Match("word[")) {
            IExpressionNode addr = ParseOrExpression();
            SkipWhitespace();
            if (PeekChar() != ']') {
                throw new ArgumentException($"Expected ']' at position {_position}");
            }
            Consume();
            return new MemoryAccessNode(addr, MemoryAccessSize.Word);
        } else if (Match("dword[")) {
            IExpressionNode addr = ParseOrExpression();
            SkipWhitespace();
            if (PeekChar() != ']') {
                throw new ArgumentException($"Expected ']' at position {_position}");
            }
            Consume();
            return new MemoryAccessNode(addr, MemoryAccessSize.Dword);
        }
        
        // Number (hex or decimal)
        if (char.IsDigit(PeekChar()) || (PeekChar() == '0' && _position + 1 < _expression.Length && char.ToLower(_expression[_position + 1]) == 'x')) {
            return ParseNumber();
        }
        
        // Variable (identifier)
        if (char.IsLetter(PeekChar()) || PeekChar() == '_') {
            return ParseVariable();
        }
        
        throw new ArgumentException($"Unexpected character at position {_position}: '{PeekChar()}'");
    }
    
    private IExpressionNode ParseNumber() {
        SkipWhitespace();
        int start = _position;
        
        // Hex number
        if (PeekChar() == '0' && _position + 1 < _expression.Length && char.ToLower(_expression[_position + 1]) == 'x') {
            _position += 2;
            while (_position < _expression.Length && IsHexDigit(_expression[_position])) {
                _position++;
            }
            string hexStr = _expression.Substring(start + 2, _position - start - 2);
            if (hexStr.Length == 0) {
                throw new ArgumentException($"Invalid hex number at position {start}");
            }
            return new ConstantNode(Convert.ToInt64(hexStr, 16));
        }
        
        // Decimal number
        while (_position < _expression.Length && char.IsDigit(_expression[_position])) {
            _position++;
        }
        string numStr = _expression.Substring(start, _position - start);
        return new ConstantNode(long.Parse(numStr));
    }
    
    private IExpressionNode ParseVariable() {
        SkipWhitespace();
        int start = _position;
        
        while (_position < _expression.Length && (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_')) {
            _position++;
        }
        
        string varName = _expression.Substring(start, _position - start);
        return new VariableNode(varName);
    }
    
    private void SkipWhitespace() {
        while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position])) {
            _position++;
        }
    }
    
    private char PeekChar() {
        return _position < _expression.Length ? _expression[_position] : '\0';
    }
    
    private bool PeekString(string str) {
        if (_position + str.Length <= _expression.Length) {
            return _expression.Substring(_position, str.Length) == str;
        }
        return false;
    }
    
    private bool Match(string str) {
        if (PeekString(str)) {
            _position += str.Length;
            return true;
        }
        return false;
    }
    
    private void Consume() {
        if (_position < _expression.Length) {
            _position++;
        }
    }
    
    private bool IsHexDigit(char c) {
        return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
    
    private bool IsStartOfNumber() {
        SkipWhitespace();
        return char.IsDigit(PeekChar()) || (PeekChar() == '0' && _position + 1 < _expression.Length && char.ToLower(_expression[_position + 1]) == 'x');
    }
}
