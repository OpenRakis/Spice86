namespace Spice86.MemoryWrappers;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Decoder for x86 instructions that provides formatted output for disassembly views.
/// </summary>
internal class InstructionsDecoder(IMemory memory, IDictionary<SegmentedAddress, FunctionInformation> functions, BreakpointsViewModel breakpointsViewModel) {
    /// <summary>
    /// Enhanced decoder that supports bidirectional decoding and custom formatting for modern views.
    /// </summary>
    /// <param name="centerAddress">The address to center the decoding around</param>
    /// <param name="blockSize">The size of the memory block around the requested address to decode</param>
    /// <returns>A list of decoded CPU instructions</returns>
    public Dictionary<uint, EnrichedInstruction> DecodeInstructions(SegmentedAddress centerAddress, uint blockSize) {
        uint halfBlockSize = blockSize / 2;

        // Let's always start at the start of the segment
        var currentAddress = new SegmentedAddress(centerAddress.Segment, 0);
        uint length = Math.Min(Math.Max(blockSize, centerAddress.Offset + halfBlockSize), 0xFFFF);

        // Read the memory block
        byte[] memoryBlock = memory.ReadRam(length, currentAddress.Linear);

        // Create a decoder for the memory block
        var codeReader = new ByteArrayCodeReader(memoryBlock);
        var decoder = Decoder.Create(16, codeReader);

        // Create a dictionary to hold the instructions
        var instructions = new Dictionary<uint, EnrichedInstruction>();

        decoder.IP = 0;
        while (currentAddress.Offset < (uint)memoryBlock.Length) {
            // Decode the instruction
            decoder.Decode(out Instruction instruction);

            // Create instruction info
            EnrichedInstruction enrichedInstruction = new(instruction) {
                Bytes = memory.ReadRam((uint)instruction.Length, currentAddress.Linear),
                Function = functions.SingleOrDefault(pair => pair.Key.Linear == currentAddress.Linear).Value,
                SegmentedAddress = currentAddress,
                Breakpoints = breakpointsViewModel.Breakpoints.Where(bp => bp.Address == currentAddress.Linear && bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS).ToList()
            };

            // Add to our collection
            instructions[currentAddress.Linear] = enrichedInstruction;

            // Move to the next instruction
            currentAddress += (ushort)instruction.Length;
        }

        return instructions;
    }
}