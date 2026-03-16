namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

public enum BinaryOperation { 
    PLUS, 
    MINUS,
    MULTIPLY, 
    DIVIDE,
    MODULO,
    EQUAL, 
    NOT_EQUAL,
    LESS_THAN,
    GREATER_THAN,
    LESS_THAN_OR_EQUAL,
    GREATER_THAN_OR_EQUAL,
    LOGICAL_AND,
    LOGICAL_OR,
    BITWISE_AND,
    BITWISE_OR,
    BITWISE_XOR,
    LEFT_SHIFT,
    RIGHT_SHIFT,
    ASSIGN 
}