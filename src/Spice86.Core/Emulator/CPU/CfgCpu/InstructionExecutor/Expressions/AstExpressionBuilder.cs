namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Memory;

using System.Linq.Expressions;
using System.Reflection;

public class AstExpressionBuilder : IAstVisitor<Expression> {
    private readonly ParameterExpression _memoryParameter = Expression.Parameter(typeof(Memory), "memory");
    private readonly ParameterExpression _stateParameter = Expression.Parameter(typeof(State), "cpuState");

    private readonly ParameterExpression[] _allParameters;

    public AstExpressionBuilder() {
        _allParameters = [_stateParameter, _memoryParameter];
    }

    private Type FromDataType(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? typeof(sbyte) : typeof(byte),
            BitWidth.WORD_16 => dataType.Signed ? typeof(short) : typeof(ushort),
            BitWidth.DWORD_32 => dataType.Signed ? typeof(int) : typeof(uint)
        };
    }

    private BinaryExpression ToExpression(Operation operation, Expression left, Expression right) {
        return operation switch {
            Operation.PLUS => Expression.Add(left, right),
            Operation.MULTIPLY => Expression.Multiply(left, right)
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
            BitWidth.DWORD_32 => dataType.Signed ? nameof(Memory.Int32) : nameof(Memory.UInt32)
        };
    }
    
    private Type ToMemoryIndexerType(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? typeof(Int8Indexer) : typeof(UInt8Indexer),
            BitWidth.WORD_16 => dataType.Signed ? typeof(Int16Indexer) : typeof(UInt16Indexer),
            BitWidth.DWORD_32 => dataType.Signed ? typeof(Int32Indexer) : typeof(UInt32Indexer)
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

    public Expression VisitSegmentRegisterNode(SegmentRegisterNode node) {
        throw new NotImplementedException();
    }
    public Expression VisitSegmentedPointer(SegmentedPointer node) {
        throw new NotImplementedException();
    }
    public Expression VisitRegisterNode(RegisterNode node) {
        throw new NotImplementedException();
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
        return ToExpression(node.Operation, left, right);
    }
    public Expression VisitInstructionNode(InstructionNode node) {
        throw new NotImplementedException();
    }
    public Expression VisitConstantNode(ConstantNode node) {
        Type type = FromDataType(node.DataType);
        return Expression.Constant(node.Value, type);
    }

    public Expression<Action<State, Memory>> ToLambda(Expression expression) {
        return Expression.Lambda<Action<State, Memory>>(expression, _allParameters);
    }
}