namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

/// <summary>
/// Helper class for creating CPU flag nodes in the AST.
/// </summary>
public class FlagAstBuilder {
    private readonly ControlFlowAstBuilder _controlFlow;

    public FlagAstBuilder(ControlFlowAstBuilder controlFlow) {
        _controlFlow = controlFlow;
    }
    /// <summary>
    /// Creates a CpuFlagNode for the Carry flag.
    /// </summary>
    public CpuFlagNode Carry() => new CpuFlagNode(Flags.Carry);

    /// <summary>
    /// Creates a CpuFlagNode for the Zero flag.
    /// </summary>
    public CpuFlagNode Zero() => new CpuFlagNode(Flags.Zero);

    /// <summary>
    /// Creates a CpuFlagNode for the Sign flag.
    /// </summary>
    public CpuFlagNode Sign() => new CpuFlagNode(Flags.Sign);

    /// <summary>
    /// Creates a CpuFlagNode for the Overflow flag.
    /// </summary>
    public CpuFlagNode Overflow() => new CpuFlagNode(Flags.Overflow);

    /// <summary>
    /// Creates a CpuFlagNode for the Parity flag.
    /// </summary>
    public CpuFlagNode Parity() => new CpuFlagNode(Flags.Parity);

    /// <summary>
    /// Creates a CpuFlagNode for the Auxiliary flag.
    /// </summary>
    public CpuFlagNode Auxiliary() => new CpuFlagNode(Flags.Auxiliary);

    /// <summary>
    /// Creates a CpuFlagNode for the Direction flag.
    /// </summary>
    public CpuFlagNode Direction() => new CpuFlagNode(Flags.Direction);

    /// <summary>
    /// Creates a CpuFlagNode for the Interrupt flag.
    /// </summary>
    public CpuFlagNode Interrupt() => new CpuFlagNode(Flags.Interrupt);

    /// <summary>
    /// Creates a CpuFlagNode for the Trap flag.
    /// </summary>
    public CpuFlagNode Trap() => new CpuFlagNode(Flags.Trap);

    /// <summary>
    /// Creates a node representing the entire flags register with the specified data type.
    /// </summary>
    /// <param name="dataType">The data type (UINT16 for 16-bit flags, UINT32 for 32-bit flags).</param>
    /// <returns>A ValueNode representing the flags register with the specified data type.</returns>
    public ValueNode FlagsRegister(DataType dataType) {
        return new FlagRegisterNode(dataType);
    }

    /// <summary>
    /// Builds an AST ValueNode for a SET condition based on the condition code.
    /// Condition codes: 0=O, 1=NO, 2=B/C, 3=AE/NC, 4=E/Z, 5=NE/NZ, 6=BE/NA, 7=A/NBE,
    ///                 8=S, 9=NS, 10=P/PE, 11=NP/PO, 12=L/NGE, 13=GE/NL, 14=LE/NG, 15=G/NLE
    /// </summary>
    /// <param name="conditionCode">The SET condition code (0-15).</param>
    /// <returns>A ValueNode representing the condition.</returns>
    public ValueNode BuildSetCondition(int conditionCode) {
        return conditionCode switch {
            0 => Overflow(),                    // O
            1 => new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Overflow()),  // NO
            2 => Carry(),                       // B/C
            3 => new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Carry()),     // AE/NC
            4 => Zero(),                        // E/Z
            5 => new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Zero()),      // NE/NZ
            6 => new BinaryOperationNode(DataType.BOOL, Carry(), BinaryOperation.LOGICAL_OR, Zero()),  // BE/NA
            7 => new BinaryOperationNode(DataType.BOOL, 
                new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Carry()), 
                BinaryOperation.LOGICAL_AND,
                new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Zero())),  // A/NBE
            8 => Sign(),                        // S
            9 => new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Sign()),      // NS
            10 => Parity(),                     // P/PE
            11 => new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Parity()),   // NP/PO
            12 => new BinaryOperationNode(DataType.BOOL, Sign(), BinaryOperation.NOT_EQUAL, Overflow()),  // L/NGE
            13 => new BinaryOperationNode(DataType.BOOL, Sign(), BinaryOperation.EQUAL, Overflow()),      // GE/NL
            14 => new BinaryOperationNode(DataType.BOOL, Zero(), BinaryOperation.LOGICAL_OR,
                new BinaryOperationNode(DataType.BOOL, Sign(), BinaryOperation.NOT_EQUAL, Overflow())),  // LE/NG
            15 => new BinaryOperationNode(DataType.BOOL, 
                new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Zero()), 
                BinaryOperation.LOGICAL_AND,
                new BinaryOperationNode(DataType.BOOL, Sign(), BinaryOperation.EQUAL, Overflow())),  // G/NLE
            _ => throw new ArgumentException($"conditionCode {conditionCode} is invalid.")
        };
    }

    /// <summary>
    /// Creates an assignment node that sets State.InterruptShadowing to true.
    /// </summary>
    public BinaryOperationNode SetInterruptShadowing() {
        MethodCallValueNode shadowingNode = new MethodCallValueNode(DataType.BOOL, nameof(State), nameof(State.InterruptShadowing));
        return new BinaryOperationNode(DataType.BOOL, shadowingNode, BinaryOperation.ASSIGN, new ConstantNode(DataType.BOOL, 1UL));
    }

    /// <summary>
    /// Creates an AST if-block that sets State.InterruptShadowing to true only when State.InterruptFlag is false.
    /// Equivalent to: if (!State.InterruptFlag) { State.InterruptShadowing = true; }
    /// </summary>
    public IfElseNode SetInterruptShadowingIfInterruptDisabled() {
        MethodCallValueNode shadowingNode = new MethodCallValueNode(DataType.BOOL, nameof(State), nameof(State.InterruptShadowing));
        BinaryOperationNode assignment = new BinaryOperationNode(DataType.BOOL, shadowingNode, BinaryOperation.ASSIGN, new ConstantNode(DataType.BOOL, 1UL));
        UnaryOperationNode condition = new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, Interrupt());
        return _controlFlow.If(condition, assignment);
    }
}
