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
    /// Decodes instructions around a center address with specified byte ranges before and after.
    /// </summary>
    /// <param name="centerAddress">The address to center the decoding around</param>
    /// <param name="bytesBefore">Number of bytes to decode before the center address</param>
    /// <param name="bytesAfter">Number of bytes to decode after the center address</param>
    /// <returns>A dictionary of decoded instructions indexed by their linear addresses</returns>
    public Dictionary<uint, EnrichedInstruction> DecodeInstructions(SegmentedAddress centerAddress, uint bytesBefore, uint bytesAfter) {
        // Calculate start address (going back by bytesBeforeCenter)
        uint startSegmentOffset = bytesBefore > centerAddress.Offset ? 0 : centerAddress.Offset - bytesBefore;
        var startAddress = new SegmentedAddress(centerAddress.Segment, (ushort)startSegmentOffset);

        // Calculate total length to read
        uint totalLength = bytesBefore + bytesAfter;
        totalLength = Math.Min(totalLength, A20Gate.EndOfHighMemoryArea - startAddress.Linear);

        // Read the memory block
        byte[] memoryBlock = memory.ReadRam(totalLength, startAddress.Linear);

        // Create a decoder for the memory block
        var codeReader = new ByteArrayCodeReader(memoryBlock);
        var decoder = Decoder.Create(16, codeReader);

        // Create a dictionary to hold the instructions
        var instructions = new Dictionary<uint, EnrichedInstruction>();

        decoder.IP = 0;

        // Current address tracker
        SegmentedAddress currentAddress = startAddress;
        const uint maxInstrLength = 15; // Maximum x86 instruction length
        // Decode until we reach the end of the memory block
        while (currentAddress.Offset < startAddress.Offset + totalLength - maxInstrLength) {
            // Decode the instruction
            decoder.Decode(out Instruction instruction);

            // Create enriched instruction
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