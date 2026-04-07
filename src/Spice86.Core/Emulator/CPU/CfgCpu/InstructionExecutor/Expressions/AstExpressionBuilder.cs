namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.Exceptions;

using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

public class AstExpressionBuilder : IAstVisitor<Expression> {
    private readonly ParameterExpression _memoryParameter = Expression.Parameter(typeof(Memory), "memory");
    private readonly ParameterExpression _stateParameter = Expression.Parameter(typeof(State), "cpuState");
    private readonly ParameterExpression _helperParameter = Expression.Parameter(typeof(InstructionExecutionHelper), "helper");

    private readonly ParameterExpression[] _allParameters;

    private readonly RegisterRenderer _registerRenderer;

    // Stack of variable scopes for nested blocks
    // Each scope is a dictionary mapping variable names to their ParameterExpressions
    private readonly Stack<Dictionary<string, ParameterExpression>> _variableScopes = new();

    public AstExpressionBuilder() {
        _registerRenderer = new RegisterRenderer(AsmRenderingConfig.CreateSpice86Style());
        _allParameters = [_stateParameter, _memoryParameter];
    }

    private static Type FromDataType(DataType dataType) {
        if (dataType == DataType.BOOL) {
            return typeof(bool);
        }
        return dataType.BitWidth switch {
            BitWidth.NIBBLE_4 => dataType.Signed ? typeof(sbyte) : typeof(byte),
            BitWidth.QUIBBLE_5 => dataType.Signed ? typeof(sbyte) : typeof(byte),
            BitWidth.BYTE_8 => dataType.Signed ? typeof(sbyte) : typeof(byte),
            BitWidth.WORD_16 => dataType.Signed ? typeof(short) : typeof(ushort),
            BitWidth.DWORD_32 => dataType.Signed ? typeof(int) : typeof(uint),
            BitWidth.QWORD_64 => dataType.Signed ? typeof(long) : typeof(ulong),
            _ => throw new UnsupportedBitWidthException(dataType.BitWidth)
        };
    }

    private Expression ToExpression(BinaryOperation binaryOperation, Expression left, Expression right, DataType resultDataType) {
        bool isShift = binaryOperation is BinaryOperation.LEFT_SHIFT or BinaryOperation.RIGHT_SHIFT;
        bool isAssign = binaryOperation == BinaryOperation.ASSIGN;

        if (isShift) {
            // .NET Expression API: shift count must be Int32
            if (right.Type != typeof(int)) {
                right = Expression.Convert(right, typeof(int));
            }
        } else if (isAssign) {
            // Assignment requires matching types
            if (left.Type != right.Type) {
                right = Expression.Convert(right, left.Type);
            }
        } else {
            // .NET Expression API: operands must have matching types
            if (left.Type != right.Type) {
                Type targetType = GetLargerType(left.Type, right.Type);
                if (left.Type != targetType) {
                    left = Expression.Convert(left, targetType);
                }
                if (right.Type != targetType) {
                    right = Expression.Convert(right, targetType);
                }
            }

            // .NET Expression API: arithmetic not supported on sub-int types
            if (IsArithmeticOperation(binaryOperation) && IsSubIntType(left.Type)) {
                left = Expression.Convert(left, typeof(int));
                right = Expression.Convert(right, typeof(int));
            }
        }

        BinaryExpression result = binaryOperation switch {
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

        // Convert result back to AST node's DataType if .NET operation returned a different type
        Type expectedType = FromDataType(resultDataType);
        if (result.Type != expectedType && !isAssign) {
            return Expression.Convert(result, expectedType);
        }
        return result;
    }

    private static bool IsArithmeticOperation(BinaryOperation op) {
        return op is BinaryOperation.PLUS or BinaryOperation.MINUS or BinaryOperation.MULTIPLY
            or BinaryOperation.DIVIDE or BinaryOperation.MODULO;
    }

    private static bool IsSubIntType(Type type) {
        return type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort);
    }

    private static Type GetLargerType(Type type1, Type type2) {
        if (type1 == type2) {
            return type1;
        }
        if (type1 == typeof(bool) || type2 == typeof(bool)) {
            return typeof(bool);
        }
        int size1 = GetTypeSize(type1);
        int size2 = GetTypeSize(type2);
        return size1 >= size2 ? type1 : type2;
    }

    private static int GetTypeSize(Type type) {
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
        return 4;
    }

    private UnaryExpression ToExpression(UnaryOperation unaryOperation, Expression value) {
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
        if (segmentExpression.Type != typeof(ushort)) {
            segmentExpression = Expression.Convert(segmentExpression, typeof(ushort));
        }
        if (offsetExpression.Type != typeof(ushort)) {
            offsetExpression = Expression.Convert(offsetExpression, typeof(ushort));
        }
        MemberExpression indexerProperty = ToMemoryIndexerProperty(dataType);
        PropertyInfo indexer = FindDualParameterIndexer(ToMemoryIndexerType(dataType));
        return Expression.Property(indexerProperty, indexer, segmentExpression, offsetExpression);
    }
    
    private MemberExpression ToRegisterProperty(int registerIndex, DataType dataType, bool isSegmentRegister) {
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
    
    public Expression VisitCpuFlagNode(CpuFlagNode node) {
        string flagPropertyName = node.FlagMask switch {
            Flags.Carry => nameof(State.CarryFlag),
            Flags.Parity => nameof(State.ParityFlag),
            Flags.Auxiliary => nameof(State.AuxiliaryFlag),
            Flags.Zero => nameof(State.ZeroFlag),
            Flags.Sign => nameof(State.SignFlag),
            Flags.Trap => nameof(State.TrapFlag),
            Flags.Interrupt => nameof(State.InterruptFlag),
            Flags.Direction => nameof(State.DirectionFlag),
            Flags.Overflow => nameof(State.OverflowFlag),
            _ => throw new InvalidOperationException($"Unknown flag mask: 0x{node.FlagMask:X4}")
        };
        PropertyInfo flagProperty = EnsureNonNull(typeof(State).GetProperty(flagPropertyName));
        return Expression.Property(_stateParameter, flagProperty);
    }

    public Expression VisitFlagRegisterNode(FlagRegisterNode node) {
        PropertyInfo flagsProperty = EnsureNonNull(typeof(State).GetProperty(nameof(State.Flags)));
        Expression flagsObject = Expression.Property(_stateParameter, flagsProperty);

        // Select the appropriate property based on the DataType
        string propertyName = node.DataType.BitWidth == BitWidth.WORD_16 
            ? nameof(Flags.FlagRegister16) 
            : nameof(Flags.FlagRegister);
        
        PropertyInfo flagRegisterProperty = EnsureNonNull(typeof(Flags).GetProperty(propertyName));
        return Expression.Property(flagsObject, flagRegisterProperty);
    }

    public Expression VisitSegmentedAddressValueNode(SegmentedAddressValueNode node) {
        Expression segment = node.Segment.Accept(this);
        Expression offset = node.Offset.Accept(this);

        ConstructorInfo constructorInfo = EnsureNonNull(typeof(SegmentedAddress).GetConstructor([typeof(ushort), typeof(ushort)]));
        return Expression.New(constructorInfo, segment, offset);
    }
    
    public Expression VisitBinaryOperationNode(BinaryOperationNode node) {
        Expression left = node.Left.Accept(this);
        Expression right = node.Right.Accept(this);
        return ToExpression(node.BinaryOperation, left, right, node.DataType);
    }
    
    public Expression VisitUnaryOperationNode(UnaryOperationNode node) {
        Expression value = node.Value.Accept(this);
        return ToExpression(node.UnaryOperation, value);
    }
    
    public Expression VisitTypeConversionNode(TypeConversionNode node) {
        Expression value = node.Value.Accept(this);
        Type targetType = FromDataType(node.DataType);
        return Expression.Convert(value, targetType);
    }
    
    public Expression VisitInstructionNode(InstructionNode node) {
        throw new InvalidOperationException(
            $"InstructionNode is for assembly parsing and rendering, not execution. " +
            $"Use HelperCallNode and BlockNode in GenerateExecutionAst() instead.");
    }

    public Expression VisitConstantNode(ConstantNode node) {
        Type type = FromDataType(node.DataType);
        object castValue = ToConstantValue(node);
        return Expression.Constant(castValue, type);
    }

    private static object ToConstantValue(ConstantNode node) {
        if (node.DataType == DataType.BOOL) {
            return node.Value != 0;
        }

        return node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 or BitWidth.QUIBBLE_5 or BitWidth.BYTE_8 =>
                node.DataType.Signed ? (object)(sbyte)node.SignedValue : (byte)node.Value,
            BitWidth.WORD_16 => node.DataType.Signed ? (object)(short)node.SignedValue : (ushort)node.Value,
            BitWidth.DWORD_32 => node.DataType.Signed ? (object)(int)node.SignedValue : (uint)node.Value,
            BitWidth.QWORD_64 => node.DataType.Signed ? (object)node.SignedValue : node.Value,
            _ => throw new UnsupportedBitWidthException(node.DataType.BitWidth)
        };
    }
    
    public Expression VisitNearAddressNode(NearAddressNode node) {
        return VisitConstantNode(node);
    }

    public Expression VisitMethodCallValueNode(MethodCallValueNode node) {
        // Delegate to the composed MethodCallNode
        return VisitMethodCallNode(node.CallNode);
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

    public Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> ToActionWithHelper(Expression expression) {
        PropertyInfo stateProperty = EnsureNonNull(typeof(InstructionExecutionHelper).GetProperty(nameof(InstructionExecutionHelper.State)));
        PropertyInfo memoryProperty = EnsureNonNull(typeof(InstructionExecutionHelper).GetProperty(nameof(InstructionExecutionHelper.Memory)));

        BlockExpression body = Expression.Block(
            [_stateParameter, _memoryParameter],
            Expression.Assign(_stateParameter, Expression.Property(_helperParameter, stateProperty)),
            Expression.Assign(_memoryParameter, Expression.Convert(Expression.Property(_helperParameter, memoryProperty), typeof(Memory))),
            expression
        );

        return Expression.Lambda<CfgNodeExecutionAction<InstructionExecutionHelper>>(body, _helperParameter);
    }

    public Expression VisitMethodCallNode(MethodCallNode node) {
        // Get the target object (helper or helper.PropertyPath)
        Expression target;
        Type targetType;
        if (node.PropertyPath == null) {
            target = _helperParameter;
            targetType = typeof(InstructionExecutionHelper);
        } else {
            PropertyInfo? helperProperty = typeof(InstructionExecutionHelper)
                .GetProperty(node.PropertyPath, BindingFlags.Public | BindingFlags.Instance);
            if (helperProperty == null) {
                throw new InvalidOperationException(
                    $"Property '{typeof(InstructionExecutionHelper).FullName}.{node.PropertyPath}' not found");
            }
            target = Expression.Property(_helperParameter, helperProperty);
            targetType = helperProperty.PropertyType;
        }

        // Convert arguments
        Expression[] argumentExpressions = node.Arguments
            .Select(arg => arg.Accept(this))
            .ToArray();

        if (argumentExpressions.Length == 0) {
            PropertyInfo? targetProperty = targetType.GetProperty(node.MethodName, BindingFlags.Public | BindingFlags.Instance);
            if (targetProperty != null) {
                return Expression.Property(target, targetProperty);
            }
        }

        (MethodInfo method, Expression[] convertedArguments) = ResolveMethodCall(targetType, node.MethodName, argumentExpressions);

        // Create the method call
        return Expression.Call(target, method, convertedArguments);
    }

    private static (MethodInfo Method, Expression[] Arguments) ResolveMethodCall(
        Type targetType,
        string methodName,
        Expression[] argumentExpressions) {
        IEnumerable<MethodInfo> candidateMethods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName && m.GetParameters().Length == argumentExpressions.Length);

        MethodInfo? bestMethod = null;
        Expression[]? bestArguments = null;
        int bestScore = int.MaxValue;

        foreach (MethodInfo candidateMethod in candidateMethods) {
            if (!TryConvertArguments(argumentExpressions, candidateMethod.GetParameters(), out Expression[] convertedArguments, out int score)) {
                continue;
            }

            if (score < bestScore) {
                bestMethod = candidateMethod;
                bestArguments = convertedArguments;
                bestScore = score;
            }
        }

        if (bestMethod != null && bestArguments != null) {
            return (bestMethod, bestArguments);
        }

        string argumentTypes = string.Join(", ", argumentExpressions.Select(e => e.Type.Name));
        throw new InvalidOperationException(
            $"Method '{targetType.FullName}.{methodName}({argumentTypes})' not found");
    }

    private static bool TryConvertArguments(
        Expression[] argumentExpressions,
        ParameterInfo[] parameters,
        out Expression[] convertedArguments,
        out int score) {
        convertedArguments = new Expression[argumentExpressions.Length];
        score = 0;

        for (int i = 0; i < argumentExpressions.Length; i++) {
            Expression argumentExpression = argumentExpressions[i];
            Type parameterType = parameters[i].ParameterType;

            if (argumentExpression.Type == parameterType) {
                convertedArguments[i] = argumentExpression;
                continue;
            }

            if (parameterType.IsAssignableFrom(argumentExpression.Type)) {
                convertedArguments[i] = Expression.Convert(argumentExpression, parameterType);
                score += 1;
                continue;
            }

            if (CanConvertExpression(argumentExpression.Type, parameterType)) {
                convertedArguments[i] = Expression.Convert(argumentExpression, parameterType);
                score += 2;
                continue;
            }

            score = int.MaxValue;
            return false;
        }

        return true;
    }

    private static bool CanConvertExpression(Type sourceType, Type targetType) {
        Type nonNullableSourceType = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        Type nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullableSourceType == nonNullableTargetType) {
            return true;
        }

        if (IsNumericType(nonNullableSourceType) && IsNumericType(nonNullableTargetType)) {
            return true;
        }

        return false;
    }

    private static bool IsNumericType(Type type) {
        return type == typeof(byte)
               || type == typeof(sbyte)
               || type == typeof(short)
               || type == typeof(ushort)
               || type == typeof(int)
               || type == typeof(uint)
               || type == typeof(long)
               || type == typeof(ulong);
    }

    public Expression VisitMoveIpNextNode(MoveIpNextNode node) {
        // State.IP = NextIp
        PropertyInfo ipProperty = EnsureNonNull(typeof(State).GetProperty(nameof(State.IP)));
        Expression ipExpression = Expression.Property(_stateParameter, ipProperty);
        Expression nextIpValue = node.NextIp.Accept(this);
        
        return Expression.Assign(ipExpression, nextIpValue);
    }

    public Expression VisitCallNearNode(CallNearNode node) {
        // helper.NearCallWithReturnIpNextInstructionXX(instruction, targetIp)
        string methodName = node.CallSize == 16 
            ? nameof(InstructionExecutionHelper.NearCallWithReturnIpNextInstruction16) 
            : nameof(InstructionExecutionHelper.NearCallWithReturnIpNextInstruction32);

        return CallHelperMethodWithInstruction(methodName, node,
            node.TargetIp.Accept(this));
    }

    public Expression VisitCallFarNode(CallFarNode node) {
        Expression targetAddress = node.TargetAddress.Accept(this);

        string methodName = node.CallSize == 16 
            ? nameof(InstructionExecutionHelper.FarCallWithReturnIpNextInstruction16) 
            : nameof(InstructionExecutionHelper.FarCallWithReturnIpNextInstruction32);

        return CallHelperMethodWithInstruction(methodName, node,
            targetAddress);
    }

    public Expression VisitReturnNearNode(ReturnNearNode node) {
        string methodName = node.RetSize == 16
            ? nameof(InstructionExecutionHelper.HandleNearRet16)
            : nameof(InstructionExecutionHelper.HandleNearRet32);
            
        return CallHelperMethodWithInstruction(methodName, node,
            node.BytesToPop.Accept(this));
    }

    public Expression VisitReturnFarNode(ReturnFarNode node) {
        string methodName = node.RetSize == 16
            ? nameof(InstructionExecutionHelper.HandleFarRet16)
            : nameof(InstructionExecutionHelper.HandleFarRet32);
            
        return CallHelperMethodWithInstruction(methodName, node,
            node.BytesToPop.Accept(this));
    }

    public Expression VisitJumpNearNode(JumpNearNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.JumpNear), node,
            node.Ip.Accept(this));
    }
    
    public Expression VisitJumpFarNode(JumpFarNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.JumpFar), node,
            node.TargetAddress.Segment.Accept(this),
            node.TargetAddress.Offset.Accept(this));
    }

    public Expression VisitHltNode(HltNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.ExecuteHlt), node);
    }

    public Expression VisitInterruptCallNode(InterruptCallNode node) {
        // HandleInterruptInstruction takes byte vectorNumber
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.HandleInterruptInstruction), node,
            node.VectorNumber.Accept(this));
    }

    public Expression VisitReturnInterruptNode(ReturnInterruptNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.HandleInterruptRet), node);
    }

    public Expression VisitCallbackNode(CallbackNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.ExecuteCallback), node,
            node.CallbackNumber.Accept(this));
    }

    public Expression VisitSelectorNode(SelectorNode node) {
        return Expression.Empty();
    }

    public Expression VisitInvalidInstructionNode(InvalidInstructionNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.HandleCpuException), node,
            Expression.Constant(node.CpuException, typeof(CpuException)));
    }

    public Expression VisitCpuidNode(CpuidNode node) {
        return CallHelperMethodWithInstruction(nameof(InstructionExecutionHelper.ExecuteCpuid), node);
    }

    private Expression CallHelperMethodWithInstruction(string methodName, CfgInstructionNode node, params Expression[] args) {
        Expression[] allArgs = new Expression[args.Length + 1];
        allArgs[0] = Expression.Constant(node.Instruction, node.Instruction.GetType());
        Array.Copy(args, 0, allArgs, 1, args.Length);
        
        return CallHelperMethod(methodName, allArgs);
    }

    private Expression CallHelperMethod(string methodName, params Expression[] arguments) {
        MethodInfo? method = typeof(InstructionExecutionHelper).GetMethod(methodName);
        if (method != null && method.IsGenericMethodDefinition) {
            method = method.MakeGenericMethod(arguments[0].Type);
        }
        if (method == null) {
            method = typeof(InstructionExecutionHelper).GetMethods()
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == arguments.Length);
                
            if (method != null && method.IsGenericMethodDefinition) {
                 method = method.MakeGenericMethod(arguments[0].Type);
            }
        }

        if (method == null) {
             throw new InvalidOperationException($"Method {methodName} not found on InstructionExecutionHelper with {arguments.Length} arguments");
        }
        
        return Expression.Call(_helperParameter, method, arguments);
    }

    public Expression VisitBlockNode(BlockNode node) {
        // Push a new scope for this block
        Dictionary<string, ParameterExpression> blockScope = new();
        _variableScopes.Push(blockScope);

        try {
            // Identify and register all VariableDeclarationNodes in this block
            List<ParameterExpression> blockVariables = RegisterVariablesInScope(node.Statements, blockScope);

            // Process all statements in order
            List<Expression> expressions = new();
            foreach (IVisitableAstNode statement in node.Statements) {
                expressions.Add(statement.Accept(this));
            }

            return CreateBlockExpression(blockVariables, expressions);
        } finally {
            // Pop the scope when exiting the block
            _variableScopes.Pop();
        }
    }

    public Expression VisitIfElseNode(IfElseNode node) {
        // Evaluate the condition
        Expression condition = node.Condition.Accept(this);

        // Ensure condition is boolean type
        if (condition.Type != typeof(bool)) {
            throw new InvalidOperationException(
                $"If/else condition must be boolean, got {condition.Type.Name}");
        }

        // Generate the true and false blocks
        Expression trueBlock = node.TrueCase.Accept(this);
        Expression falseBlock = node.FalseCase.Accept(this);

        // Create the conditional expression
        return Expression.IfThenElse(condition, trueBlock, falseBlock);
    }

    public Expression VisitWhileNode(WhileNode node) {
        // Evaluate the condition
        Expression condition = node.Condition.Accept(this);

        // Ensure condition is boolean type
        if (condition.Type != typeof(bool)) {
            throw new InvalidOperationException(
                $"While condition must be boolean, got {condition.Type.Name}");
        }

        // Generate the body block
        Expression body = node.Body.Accept(this);

        // Create a break label for exiting the loop
        LabelTarget breakLabel = Expression.Label();

        // Create the loop: while (condition) { body }
        // This is implemented as: Loop(IfThenElse(condition, body, Break(breakLabel)), breakLabel)
        return Expression.Loop(
            Expression.Block(
                Expression.IfThen(
                    Expression.Not(condition),
                    Expression.Break(breakLabel)
                ),
                body
            ),
            breakLabel
        );
    }

    private List<ParameterExpression> RegisterVariablesInScope(
        IReadOnlyList<IVisitableAstNode> statements,
        Dictionary<string, ParameterExpression> scope) {
        List<ParameterExpression> variables = [];

        foreach (VariableDeclarationNode varDecl in statements.OfType<VariableDeclarationNode>()) {
            Type varType = FromDataType(varDecl.DataType);
            ParameterExpression variable = Expression.Variable(varType, varDecl.VariableName);
            // Register variable for block
            scope[varDecl.VariableName] = variable;
            variables.Add(variable);
        }

        return variables;
    }

    private static BlockExpression CreateBlockExpression(
        List<ParameterExpression> variables,
        List<Expression> expressions) {
        if (expressions.Count == 0) {
            return Expression.Block(Expression.Empty());
        }
        if (variables.Count > 0) {
            return Expression.Block(variables, expressions);
        }
        return Expression.Block(expressions);
    }

    public Expression VisitVariableReferenceNode(VariableReferenceNode node) {
        // Search for the variable in the scope stack (innermost to outermost)
        foreach (Dictionary<string, ParameterExpression> scope in _variableScopes) {
            if (scope.TryGetValue(node.VariableName, out ParameterExpression? variable)) {
                return variable;
            }
        }
        throw new InvalidOperationException($"Variable '{node.VariableName}' not found in any scope");
    }

    public Expression VisitVariableDeclarationNode(VariableDeclarationNode node) {
        // Get the variable from the current scope (top of stack)
        if (_variableScopes.Count == 0) {
            throw new InvalidOperationException($"Variable '{node.VariableName}' declared outside of any block");
        }

        Dictionary<string, ParameterExpression> currentScope = _variableScopes.Peek();
        if (!currentScope.TryGetValue(node.VariableName, out ParameterExpression? variable)) {
            throw new InvalidOperationException($"Variable '{node.VariableName}' was not pre-declared in current scope");
        }

        // Generate the initialization assignment: variable = initializer
        Expression initializerExpr = node.Initializer.Accept(this);
        if (initializerExpr.Type != variable.Type) {
            initializerExpr = Expression.Convert(initializerExpr, variable.Type);
        }
        return Expression.Assign(variable, initializerExpr);
    }

    public Expression VisitThrowNode(ThrowNode node) {
        ConstructorInfo ctorInfo = EnsureNonNull(node.ExceptionType.GetConstructor([typeof(string)]));
        Expression newException = Expression.New(ctorInfo, Expression.Constant(node.Message, typeof(string)));
        return Expression.Throw(newException);
    }
}