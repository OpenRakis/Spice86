namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Memory;

using System.Linq.Expressions;
using System.Reflection;

public class AstExpressionBuilder : IAstVisitor<Expression> {
    private readonly ParameterExpression _memoryParameter = Expression.Parameter(typeof(Memory), "memory");
    private readonly ParameterExpression _stateParameter = Expression.Parameter(typeof(State), "cpuState");

    private readonly ParameterExpression[] _allParameters;

    private readonly RegisterRenderer _registerRenderer = new();

    public AstExpressionBuilder() {
        _allParameters = [_stateParameter, _memoryParameter];
    }

    private Type FromDataType(DataType dataType) {
        if (dataType == DataType.BOOL) {
            return typeof(bool);
        }
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? typeof(sbyte) : typeof(byte),
            BitWidth.WORD_16 => dataType.Signed ? typeof(short) : typeof(ushort),
            BitWidth.DWORD_32 => dataType.Signed ? typeof(int) : typeof(uint),
            _ => throw new UnsupportedBitWidthException(dataType.BitWidth)
        };
    }

    private Expression ToExpression(BinaryOperation binaryOperation, Expression left, Expression right) {
        // For comparison, logical, and bitwise operations, convert operands to a common type if needed
        if (left.Type != right.Type && binaryOperation != BinaryOperation.ASSIGN) {
            // Convert to the larger type
            Type targetType = GetLargerType(left.Type, right.Type);
            if (left.Type != targetType) {
                left = Expression.Convert(left, targetType);
            }
            if (right.Type != targetType) {
                right = Expression.Convert(right, targetType);
            }
        }
        
        return binaryOperation switch {
            BinaryOperation.PLUS => Expression.Add(left, right),
            BinaryOperation.MINUS => Expression.Subtract(left, right),
            BinaryOperation.MULTIPLY => Expression.Multiply(left, right),
            BinaryOperation.DIVIDE => Expression.Divide(left, right),
            BinaryOperation.MODULO => Expression.Modulo(left, right),
            BinaryOperation.EQUAL => Expression.Equal(left, right),
            BinaryOperation.NOT_EQUAL => Expression.NotEqual(left, right),
            BinaryOperation.LESS_THAN => Expression.LessThan(left, right),
            BinaryOperation.GREATER_THAN => Expression.GreaterThan(left, right),
            BinaryOperation.LESS_THAN_OR_EQUAL => Expression.LessThanOrEqual(left, right),
            BinaryOperation.GREATER_THAN_OR_EQUAL => Expression.GreaterThanOrEqual(left, right),
            BinaryOperation.LOGICAL_AND => Expression.AndAlso(left, right),
            BinaryOperation.LOGICAL_OR => Expression.OrElse(left, right),
            BinaryOperation.BITWISE_AND => Expression.And(left, right),
            BinaryOperation.BITWISE_OR => Expression.Or(left, right),
            BinaryOperation.BITWISE_XOR => Expression.ExclusiveOr(left, right),
            BinaryOperation.LEFT_SHIFT => Expression.LeftShift(left, right),
            BinaryOperation.RIGHT_SHIFT => Expression.RightShift(left, right),
            BinaryOperation.ASSIGN => Expression.Assign(left, right),
            _ => throw new InvalidOperationException($"Unhandled Operation: {binaryOperation}")
        };
    }
    
    private Type GetLargerType(Type type1, Type type2) {
        // For boolean types, keep them as-is
        if (type1 == typeof(bool) || type2 == typeof(bool)) {
            return typeof(bool);
        }
        
        // Order types by size: byte < ushort < uint < ulong
        int size1 = GetTypeSize(type1);
        int size2 = GetTypeSize(type2);
        return size1 >= size2 ? type1 : type2;
    }
    
    private int GetTypeSize(Type type) {
        if (type == typeof(byte) || type == typeof(sbyte)) {
            return 1;
        }
        if (type == typeof(ushort) || type == typeof(short)) {
            return 2;
        }
        if (type == typeof(uint) || type == typeof(int)) {
            return 4;
        }
        if (type == typeof(ulong) || type == typeof(long)) {
            return 8;
        }
        return 4; // Default to 32-bit
    }
    
    private Expression ToExpression(UnaryOperation unaryOperation, Expression value) {
        return unaryOperation switch {
            UnaryOperation.NOT => Expression.Not(value),
            UnaryOperation.NEGATE => Expression.Negate(value),
            UnaryOperation.BITWISE_NOT => Expression.OnesComplement(value),
            _ => throw new InvalidOperationException($"Unhandled Operation: {unaryOperation}")
        };
    }
    
    private T EnsureNonNull<T>(T? argument) {
        ArgumentNullException.ThrowIfNull(argument);
        return argument;
    }

    private string ToMemoryPropertyName(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? nameof(Memory.Int8) : nameof(Memory.UInt8),
            BitWidth.WORD_16 => dataType.Signed ? nameof(Memory.Int16) : nameof(Memory.UInt16),
            BitWidth.DWORD_32 => dataType.Signed ? nameof(Memory.Int32) : nameof(Memory.UInt32),
            _ => throw new UnsupportedBitWidthException(dataType.BitWidth)
        };
    }

    private Type ToMemoryIndexerType(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? typeof(Int8Indexer) : typeof(UInt8Indexer),
            BitWidth.WORD_16 => dataType.Signed ? typeof(Int16Indexer) : typeof(UInt16Indexer),
            BitWidth.DWORD_32 => dataType.Signed ? typeof(Int32Indexer) : typeof(UInt32Indexer),
            _ => throw new UnsupportedBitWidthException(dataType.BitWidth)
        };
    }

    private PropertyInfo FindSingleParameterIndexer(Type type) {
        PropertyInfo[] propertyInfos = type.GetProperties();
        foreach (PropertyInfo propertyInfo in propertyInfos) {
            ParameterInfo[] indexParameters = propertyInfo.GetIndexParameters();
            if (indexParameters.Length == 1 && indexParameters[0].ParameterType == typeof(uint)) {
                return propertyInfo;
            }
        }
        throw new ArgumentException($"Couldn't find a property named Item with 1 parameter for type {type}");
    }
    
    private PropertyInfo FindDualParameterIndexer(Type type) {
        PropertyInfo[] propertyInfos = type.GetProperties();
        foreach (PropertyInfo propertyInfo in propertyInfos) {
            ParameterInfo[] indexParameters = propertyInfo.GetIndexParameters();
            if (indexParameters.Length == 2 && indexParameters[0].ParameterType == typeof(ushort) && indexParameters[1].ParameterType == typeof(ushort)) {
                return propertyInfo;
            }
        }
        throw new ArgumentException($"Couldn't find a property named Item with 2 parameters for type {type}");
    }

    private MemberExpression ToMemoryIndexerProperty(DataType dataType) {
        string propertyName = ToMemoryPropertyName(dataType);
        PropertyInfo memoryIndexerProperty = EnsureNonNull(typeof(Memory).GetProperty(propertyName));
        return Expression.Property(_memoryParameter, memoryIndexerProperty);
    }

    private IndexExpression ToMemoryIndexer(DataType dataType, Expression indexExpression) {
        MemberExpression indexerProperty = ToMemoryIndexerProperty(dataType);
        PropertyInfo indexer = FindSingleParameterIndexer(ToMemoryIndexerType(dataType));
        return Expression.Property(indexerProperty, indexer, indexExpression);
    }
    
    private IndexExpression ToMemoryIndexer(DataType dataType, Expression segmentExpression, Expression offsetExpression ) {
        MemberExpression indexerProperty = ToMemoryIndexerProperty(dataType);
        PropertyInfo indexer = FindDualParameterIndexer(ToMemoryIndexerType(dataType));
        return Expression.Property(indexerProperty, indexer, segmentExpression, offsetExpression);
    }
    
    private Expression ToRegisterProperty(int registerIndex, DataType dataType, bool isSegmentRegister) {
        string name = isSegmentRegister ? _registerRenderer.ToStringSegmentRegister(registerIndex) : _registerRenderer.ToStringRegister(dataType.BitWidth, registerIndex);
        PropertyInfo stateRegisterProperty = EnsureNonNull(typeof(State).GetProperty(name));
        return Expression.Property(_stateParameter, stateRegisterProperty);
    }

    public Expression VisitSegmentRegisterNode(SegmentRegisterNode node) {
       return ToRegisterProperty(node.RegisterIndex, node.DataType, true);
    }

    public Expression VisitSegmentedPointer(SegmentedPointerNode node) {
        Expression segmentExpression = node.Segment.Accept(this);
        Expression offsetExpression = node.Offset.Accept(this);
        return ToMemoryIndexer(node.DataType, segmentExpression, offsetExpression);
    }

    public Expression VisitRegisterNode(RegisterNode node) {
        return ToRegisterProperty(node.RegisterIndex, node.DataType, false);
    }

    public Expression VisitAbsolutePointerNode(AbsolutePointerNode node) {
        Expression index = node.AbsoluteAddress.Accept(this);
        return ToMemoryIndexer(node.DataType, index);
    }
    
    public Expression VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        throw new NotImplementedException();
    }
    
    public Expression VisitBinaryOperationNode(BinaryOperationNode node) {
        Expression left = node.Left.Accept(this);
        Expression right = node.Right.Accept(this);
        return ToExpression(node.BinaryOperation, left, right);
    }
    
    public Expression VisitUnaryOperationNode(UnaryOperationNode node) {
        Expression value = node.Value.Accept(this);
        return ToExpression(node.UnaryOperation, value);
    }
    
    public Expression VisitInstructionNode(InstructionNode node) {
        throw new NotImplementedException();
    }

    public Expression VisitConstantNode(ConstantNode node) {
        Type type = FromDataType(node.DataType);
        object castValue = Convert.ChangeType(node.Value, type);
        return Expression.Constant(castValue, type);
    }

    public Expression<Action<State, Memory>> ToAction(Expression expression) {
        return Expression.Lambda<Action<State, Memory>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, byte>> ToFuncUInt8(Expression expression) {
        return Expression.Lambda<Func<State, Memory, byte>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, sbyte>> ToFuncInt8(Expression expression) {
        return Expression.Lambda<Func<State, Memory, sbyte>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, ushort>> ToFuncUInt16(Expression expression) {
        return Expression.Lambda<Func<State, Memory, ushort>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, short>> ToFuncInt16(Expression expression) {
        return Expression.Lambda<Func<State, Memory, short>>(expression, _allParameters);
    }

    public Expression<Func<State, Memory, uint>> ToFuncUInt32(Expression expression) {
        return Expression.Lambda<Func<State, Memory, uint>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, int>> ToFuncInt32(Expression expression) {
        return Expression.Lambda<Func<State, Memory, int>>(expression, _allParameters);
    }
    
    public Expression<Func<State, Memory, bool>> ToFuncBool(Expression expression) {
        return Expression.Lambda<Func<State, Memory, bool>>(expression, _allParameters);
    }
}