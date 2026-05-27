namespace Spice86.Tests.CfgCpu.Blocks;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

/// <summary>
/// Shared factory helpers for CFG block tests.
/// </summary>
internal static class CfgTestHelpers {
    private static readonly SequentialIdAllocator _allocator = new();

    /// <summary>
    /// Builds a synthetic NOP <see cref="CfgInstruction"/> at <paramref name="address"/>.
    /// </summary>
    internal static CfgInstruction CreateInstruction(SegmentedAddress address) {
        return CreateInstruction(address, 0x90, 1, InstructionKind.None);
    }

    /// <summary>
    /// Builds a synthetic <see cref="CfgInstruction"/> at <paramref name="address"/> with the
    /// specified <paramref name="opcode"/>, <paramref name="length"/>, and <paramref name="kind"/>.
    /// </summary>
    internal static CfgInstruction CreateInstruction(SegmentedAddress address, byte opcode, int length, InstructionKind kind) {
        InstructionField<ushort> opcodeField = new(
            indexInInstruction: 0,
            length: length,
            physicalAddress: address.Linear,
            value: opcode,
            signatureValue: ImmutableList.Create<byte?>(opcode),
            final: true);
        return new(_allocator.AllocateId(), address, opcodeField, new List<InstructionPrefix>(), maxSuccessorsCount: 1) {
            Kind = kind
        };
    }

    /// <summary>
    /// Returns the <see cref="CfgBlock"/> that contains <paramref name="node"/>,
    /// or throws if the node has no containing block.
    /// </summary>
    internal static CfgBlock GetContainingBlock(ICfgNode node) {
        if (node.ContainingBlock is CfgBlock block) {
            return block;
        }
        throw new InvalidOperationException($"Node at {node.Address} has no containing block.");
    }
}
