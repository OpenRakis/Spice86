namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Rebuilds individual <see cref="ICfgNode"/>s from their serialized form by re-parsing each node's
/// own stored bytes through the existing <see cref="InstructionParser"/> and replaying the
/// <see cref="Spice86.Core.Emulator.CPU.CfgCpu.Feeder.SignatureReducer"/> post-processing
/// (<c>NullifySignature()</c> + <c>UseValue = false</c>) on the modified-immediate fields.
///
/// The authoritative byte source is the node itself, never the restart-time memory image (which
/// differs from dump time and cannot reproduce self-modified variants). See the persistence plan.
/// </summary>
internal sealed class CfgNodeReconstructor {
    private readonly CfgReloadParseMemory _parseMemory = new();
    private readonly InstructionParser _instructionParser;
    private readonly SequentialIdAllocator _parseIdAllocator = new();
    private readonly CfgNodeExecutionCompiler _executionCompiler;

    public CfgNodeReconstructor(State state, CfgNodeExecutionCompiler executionCompiler) {
        _executionCompiler = executionCompiler;
        // Re-parse only ever reads from the byte buffer we write per node, never from the live bus.
        _instructionParser = new InstructionParser(_parseMemory, state, _parseIdAllocator);
    }

    /// <summary>
    /// Reconstructs a node, preserving its dumped id verbatim, and compiles its execution.
    /// </summary>
    public ICfgNode Reconstruct(CfgReloadNodeInfo nodeInfo) {
        SegmentedAddress address = ParseAddress(nodeInfo.Addr);
        ICfgNode node = nodeInfo.Type switch {
            CfgReloadNodeType.Selector => ReconstructSelector(nodeInfo.Id, address),
            CfgReloadNodeType.Instruction => ReconstructInstruction(nodeInfo, address),
            _ => throw new InvalidOperationException($"Unknown reload node type '{nodeInfo.Type}' for node {nodeInfo.Id}")
        };
        _executionCompiler.Compile(node);
        return node;
    }

    private SelectorNode ReconstructSelector(int id, SegmentedAddress address) {
        return new SelectorNode(id, address);
    }

    private CfgInstruction ReconstructInstruction(CfgReloadNodeInfo nodeInfo, SegmentedAddress address) {
        if (nodeInfo.Bytes is null) {
            throw new InvalidOperationException($"Instruction node {nodeInfo.Id} at {nodeInfo.Addr} has no bytes");
        }
        byte?[] storedBytes = SigHex.Decode(nodeInfo.Bytes);

        // Write this node's bytes (placeholder 0 for __) into the parse buffer, then parse.
        // Done per node because variant instructions and a selector can share an address.
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        _parseMemory.WriteNodeBytes(physicalAddress, storedBytes);

        // Re-parse the node's own stored bytes through the normal parser.
        CfgInstruction parsed = ParsePreservingDumpedId(address, nodeInfo.Id);

        // No-op for invalid nodes (their span has no __ bytes); for normal self-modified variants,
        // nullifies the modified-immediate fields' signature and marks them as live-read.
        NullifySignatureAndUseValue(parsed, storedBytes);

        parsed.MaxSuccessorsCount = nodeInfo.MaxSucc;
        return parsed;
    }

    /// <summary>
    /// Parses the bytes already written for this node, guaranteeing the resulting node's
    /// <see cref="ICfgNode.Id"/> equals <paramref name="dumpedId"/>.
    ///
    /// A single <see cref="InstructionParser.ParseInstructionAt"/> call does not always allocate exactly
    /// one id: a decode that faults mid/post-decode (LOCK validation, ModRM mode-3, MOV-to-CS, ...) first
    /// allocates an id for the partially-built instruction, then allocates a second id for the invalid
    /// node the catch block builds. The id count a given byte span consumes is fully determined by that
    /// span, so we measure it with a first parse, then re-seed the allocator so a second parse of the
    /// same bytes lands its final node exactly on <paramref name="dumpedId"/>. The live parser keeps
    /// allocating the same way; only this private allocator is rewound.
    /// </summary>
    private CfgInstruction ParsePreservingDumpedId(SegmentedAddress address, int dumpedId) {
        _parseIdAllocator.NextId = dumpedId;
        CfgInstruction firstParse = _instructionParser.ParseInstructionAt(address);
        int idsConsumed = firstParse.Id - dumpedId;
        if (idsConsumed == 0) {
            return firstParse;
        }
        // Re-seed by the measured id count so the final node of the second parse gets exactly dumpedId.
        _parseIdAllocator.NextId = dumpedId - idsConsumed;
        CfgInstruction secondParse = _instructionParser.ParseInstructionAt(address);
        if (secondParse.Id != dumpedId) {
            throw new InvalidOperationException(
                $"Reconstruction could not preserve dumped id {dumpedId} for node at {address}: " +
                $"id allocation for this byte span is not deterministic (got {secondParse.Id}).");
        }
        return secondParse;
    }

    /// <summary>
    /// Mirrors <c>SignatureReducer.ReduceNonFinalFields</c>: for each field whose stored byte image
    /// contains a <c>__</c> (null) byte, mark it as a live-read field (<c>UseValue = false</c>) and
    /// nullify its signature. A <c>__</c> always covers a whole non-final field, because the reducer
    /// nullifies per field.
    /// </summary>
    private static void NullifySignatureAndUseValue(CfgInstruction instruction, byte?[] storedBytes) {
        int offset = 0;
        foreach (FieldWithValue field in instruction.FieldsInOrder) {
            int fieldLength = field.SignatureValue.Count;
            bool hasNull = false;
            for (int i = 0; i < fieldLength; i++) {
                int position = offset + i;
                if (position < storedBytes.Length && storedBytes[position] is null) {
                    hasNull = true;
                    break;
                }
            }
            if (hasNull) {
                field.UseValue = false;
                field.NullifySignature();
            }
            offset += fieldLength;
        }
    }

    private static SegmentedAddress ParseAddress(string addr) {
        return ReloadAddressParser.ParseOrThrow(addr, "node address");
    }
}
