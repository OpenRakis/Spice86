namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Base of all the instructions: Prefixes (optional) and an opcode that can be either one or 2 bytes.
/// </summary>
public abstract class CfgInstruction : CfgNode, ICfgInstruction {
    /// <summary>
    /// Instructions are born live.
    /// </summary>
    private bool _isLive = true;

    protected CfgInstruction(SegmentedAddress address, InstructionField<ushort> opcodeField) : this(address,
        opcodeField, new List<InstructionPrefix>()) {
    }

    protected CfgInstruction(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes)
        : base(address) {
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

    public override bool IsLive => _isLive;

    public List<FieldWithValue> FieldsInOrder { get; } = new();

    protected void AddField(FieldWithValue fieldWithValue) {
        FieldsInOrder.Add(fieldWithValue);
        UpdateLength();
    }
    
    protected void AddFields(IEnumerable<FieldWithValue> fieldWithValues) {
        fieldWithValues.ToList().ForEach(AddField);
    }

    public SegmentOverrideInstructionPrefix? SegmentOverrideInstructionPrefix { get; }
    public OperandSize32Prefix? OperandSize32Prefix { get; }
    public AddressSize32Prefix? AddressSize32Prefix { get; }
    public RepPrefix? RepPrefix { get; }

    public byte Length { get; private set; }

    public SegmentedAddress NextInMemoryAddress => new(Address.Segment, (ushort)(Address.Offset + Length));

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
    public Discriminator Discriminator {
        get {
            ImmutableList<byte?> discriminatorBytes = ComputeDiscriminatorBytes(FieldsInOrder);
            return new Discriminator(discriminatorBytes);
        }
    }

    /// <summary>
    /// Same as Discriminator but only aggregates final fields, ignoring those that can change.
    /// </summary>
    public Discriminator DiscriminatorFinal {
        get {
            ImmutableList<byte?> discriminatorBytes = ComputeDiscriminatorBytes(FieldsInOrder
                .Where(field => field.Final));
            return new Discriminator(discriminatorBytes);
        }
    }

    private ImmutableList<byte?> ComputeDiscriminatorBytes(IEnumerable<FieldWithValue> bytes) {
        return bytes
            .Select(field => field.DiscriminatorValue)
            .SelectMany(i => i)
            .ToImmutableList();
    }
    
    public void SetLive(bool isLive) {
        _isLive = isLive;
    }
}