namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using InstructionNode = Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.InstructionNode;

using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Base of all the instructions: Prefixes (optional) and an opcode that can be either one or 2 bytes.
/// </summary>
public sealed class CfgInstruction : CfgNode {
    /// <summary>
    /// Classifies the control-flow role of this instruction.
    /// </summary>
    public InstructionKind Kind { get; init; } = InstructionKind.None;

    /// <summary>True when this instruction is a RET or IRET.</summary>
    public bool IsReturn => Kind.HasFlag(InstructionKind.Return);

    /// <summary>True when this instruction is a CALL.</summary>
    public bool IsCall => Kind.HasFlag(InstructionKind.Call);

    /// <summary>True when this instruction is a JMP (any form).</summary>
    public bool IsJump => Kind.HasFlag(InstructionKind.Jump);

    /// <summary>True when this instruction represents an invalid opcode.</summary>
    public bool IsInvalid => Kind.HasFlag(InstructionKind.Invalid);

    private bool _canCauseContextRestore;

    /// <inheritdoc />
    public override bool CanCauseContextRestore => _canCauseContextRestore;

    /// <summary>Marks this instruction as one that can restore execution context (e.g., IRET).</summary>
    internal void EnableCanCauseContextRestore() => _canCauseContextRestore = true;

    /// <summary>
    /// The call instruction that corresponds to this return instruction.
    /// Only meaningful when <see cref="IsReturn"/> is <c>true</c>.
    /// </summary>
    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    private InstructionNode? _instructionAst;
    private IVisitableAstNode? _executionAst;

    /// <summary>
    /// Attaches pre-built ASTs produced by the parser.
    /// Must be called exactly once per instruction, immediately after all fields are registered.
    /// </summary>
    internal void AttachAsts(InstructionNode instructionAst, IVisitableAstNode executionAst) {
        _instructionAst = instructionAst;
        _executionAst = executionAst;
    }

    /// <summary>
    /// Instructions are born live.
    /// </summary>
    private bool _isLive = true;

    public CfgInstruction(SegmentedAddress address, InstructionField<ushort> opcodeField, int? maxSuccessorsCount) : this(address,
        opcodeField, new List<InstructionPrefix>(), maxSuccessorsCount) {
    }

    public CfgInstruction(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, int? maxSuccessorsCount)
        : base(address, maxSuccessorsCount) {
        InstructionPrefixes = prefixes;
        PrefixFields = prefixes.Select(prefix => prefix.PrefixField).ToList();
        foreach (InstructionPrefix prefix in prefixes) {
            if (prefix is SegmentOverrideInstructionPrefix instructionPrefix) {
                SegmentOverrideInstructionPrefix = instructionPrefix;
            } else if (prefix is OperandSize32Prefix size32Prefix) {
                OperandSize32Prefix = size32Prefix;
            } else if (prefix is AddressSize32Prefix addressSize32Prefix) {
                AddressSize32Prefix = addressSize32Prefix;
            } else if (prefix is RepPrefix repPrefix) {
                RepPrefix = repPrefix;
            } else if (prefix is LockPrefix lockPrefix) {
                LockPrefix = lockPrefix;
            }
        }

        OpcodeField = opcodeField;
        AddFields(PrefixFields);
        AddField(OpcodeField);
    }

    /// <summary>
    /// To call after constructor to calculate instruction length
    /// </summary>
    private void UpdateLength() {
        Length = (byte)FieldsInOrder.Sum(field => field.Length);
        NextInMemoryAddress = new(Address.Segment, (ushort)(Address.Offset + Length));
    }

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public Dictionary<SegmentedAddress, ICfgNode> SuccessorsPerAddress { get; private set; } = new();

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public Dictionary<InstructionSuccessorType, ISet<ICfgNode>> SuccessorsPerType { get; } = new();

    public override void UpdateSuccessorCache() {
        SuccessorsPerAddress = Successors.ToDictionary(node => node.Address);
    }

    public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) {
        if (UniqueSuccessor is not null) {
            return UniqueSuccessor;
        }
        SuccessorsPerAddress.TryGetValue(helper.State.IpSegmentedAddress, out ICfgNode? res);
        return res;
    }

    public override bool IsLive => _isLive;

    public List<FieldWithValue> FieldsInOrder { get; } = new();

    internal void AddField(FieldWithValue fieldWithValue) {
        FieldsInOrder.Add(fieldWithValue);
        UpdateLength();
    }

    internal void AddFields(IEnumerable<FieldWithValue> fieldWithValues) {
        fieldWithValues.ToList().ForEach(AddField);
    }

    public SegmentOverrideInstructionPrefix? SegmentOverrideInstructionPrefix { get; }
    public OperandSize32Prefix? OperandSize32Prefix { get; }
    public AddressSize32Prefix? AddressSize32Prefix { get; }
    public RepPrefix? RepPrefix { get; }

    public LockPrefix? LockPrefix { get; }
    public byte Length { get; private set; }

    public SegmentedAddress NextInMemoryAddress { get; private set; }

    public List<InstructionPrefix> InstructionPrefixes { get; }

    /// <summary>
    /// List of prefixes for this instruction
    /// </summary>
    public List<InstructionField<byte>> PrefixFields { get; }

    /// <summary>
    /// Opcode
    /// </summary>
    public InstructionField<ushort> OpcodeField { get; }

    /// <summary>
    /// What allows to uniquely identify the instruction among other at the same address.
    /// Usually all the fields except in some cases when they are modified (example imm value or disp), in this case instead of bytes there will be nulls
    /// </summary>
    public Signature Signature {
        get {
            ImmutableList<byte?> signatureBytes = ComputeSignatureBytes(FieldsInOrder);
            return new Signature(signatureBytes);
        }
    }

    /// <summary>
    /// Same as Signature but only aggregates final fields, ignoring those that can change.
    /// </summary>
    public Signature SignatureFinal {
        get {
            ImmutableList<byte?> signatureBytes = ComputeSignatureBytes(FieldsInOrder
                .Where(x => x.Final));
            return new Signature(signatureBytes);
        }
    }

    private ImmutableList<byte?> ComputeSignatureBytes(IEnumerable<FieldWithValue> bytes) {
        return bytes
            .Select(field => field.SignatureValue)
            .SelectMany(i => i)
            .ToImmutableList();
    }
    
    public void SetLive(bool isLive) {
        _isLive = isLive;
    }

    /// <summary>
    /// Returns the pre-built display AST set by <see cref="AttachAsts"/>.
    /// </summary>
    public override InstructionNode DisplayAst {
        get {
            if (_instructionAst is not null) {
                return _instructionAst;
            }
            throw new InvalidOperationException(
                $"AttachAsts must be called before accessing DisplayAst for {GetType().Name} at {Address}");
        }
    }

    /// <summary>
    /// Returns the pre-built execution AST set by <see cref="AttachAsts"/>.
    /// </summary>
    public override IVisitableAstNode ExecutionAst {
        get {
            if (_executionAst is not null) {
                return _executionAst;
            }
            throw new InvalidOperationException(
                $"AttachAsts must be called before accessing ExecutionAst for {GetType().Name} at {Address}");
        }
    }

    public void IncreaseMaxSuccessorsCount(SegmentedAddress target) {
        if (MaxSuccessorsCount is not null && !SuccessorsPerAddress.ContainsKey(target)) {
            // Ensure the subsequent link attempt will be done
            CanHaveMoreSuccessors = true;
            MaxSuccessorsCount++;
            // Reset it. Will not be used anymore if MaxSuccessorsCount is now more than 1 or null.
            UniqueSuccessor = null;
        }
    }
}