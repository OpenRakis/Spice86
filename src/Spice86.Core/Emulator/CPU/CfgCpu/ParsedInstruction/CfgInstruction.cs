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
        FieldsInOrder.AddRange(PrefixFields);
        FieldsInOrder.Add(OpcodeField);
    }

    /// <summary>
    /// To call after constructor to calculate instruction length
    /// </summary>
    public void PostInit() {
        Length = (byte)FieldsInOrder.Sum(field => field.Length);
    }

    /// <summary>
    /// Cache of Successors property per address. Maintenance is complex with self modifying code and is done by the InstructionLinker
    /// </summary>
    public Dictionary<SegmentedAddress, ICfgNode> SuccessorsPerAddress { get; private set; } = new();

    /// <summary>
    /// Successors per link type
    /// This allows to represent the link between a call instruction and the effective return address.
    /// This is present for all instructions since most of them can trigger CPU faults (and interrupt calls)
    /// </summary>
    public Dictionary<InstructionSuccessorType, ISet<ICfgNode>> SuccessorsPerType { get; } = new();

    public bool ReturnWasToOneOfCaller;

    public override void UpdateSuccessorCache() {
        SuccessorsPerAddress = Successors.ToDictionary(node => node.Address);
    }

    public override bool IsAssembly { get => true; }

    public List<FieldWithValue> FieldsInOrder { get; } = new();

    public SegmentOverrideInstructionPrefix? SegmentOverrideInstructionPrefix { get; }
    public OperandSize32Prefix? OperandSize32Prefix { get; }
    public AddressSize32Prefix? AddressSize32Prefix { get; }
    public RepPrefix? RepPrefix { get; }

    public byte Length { get; private set; }

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

// Equals and HashCode to use the discriminator and super methods
}