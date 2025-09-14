namespace Spice86.ViewModels.Services;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.ViewModels.TextPresentation;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Decoder for x86 instructions that provides formatted output for disassembly views.
/// </summary>
internal class InstructionsDecoder(IMemory memory, IDictionary<SegmentedAddress, FunctionInformation> functions, BreakpointsViewModel breakpointsViewModel) {
    /// <summary>
    /// Length of the callback opcode sequence (FE 38 XX XX)
    /// </summary>
    private const int CallbackOpcodeLength = 4;

    private const DecoderOptions X86DecoderOptions = DecoderOptions.Loadall286 | DecoderOptions.Loadall386;

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

        // Create a dictionary to hold the instructions
        var instructions = new Dictionary<uint, EnrichedInstruction>();

        // Calculate the offset of the center address within the memory block
        int centerAddressOffset = (int)(centerAddress.Linear - startAddress.Linear);

        // First, decode instructions before the center address
        DecodeInstructionsBefore(memoryBlock, centerAddressOffset, startAddress, centerAddress, instructions);

        // Then, decode instructions from the center address onward
        // This will overwrite any overlapping instructions from the first pass
        DecodeInstructionsFrom(memoryBlock, centerAddressOffset, (int)(totalLength - centerAddressOffset), centerAddress, instructions);

        return instructions;
    }

    /// <summary>
    /// Checks if the bytes at the given offset match the callback opcode pattern (FE 38 XX).
    /// </summary>
    private static bool IsCallbackOpcode(byte[] memoryBlock, int offset, out ushort opcodeIndex) {
        opcodeIndex = 0;

        if (offset + 2 >= memoryBlock.Length) {
            return false;
        }

        if (memoryBlock[offset] == 0xFE && memoryBlock[offset + 1] == 0x38) {
            opcodeIndex = (ushort)(memoryBlock[offset + 2] | memoryBlock[offset + 3] << 8);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates an enriched instruction for a callback opcode at the given address.
    /// </summary>
    private EnrichedInstruction CreateCallbackOpcodeInstruction(SegmentedAddress address, ushort callbackIndex) {
        var customInstruction = new Instruction {
            Code = Code.INVALID,
            Length = CallbackOpcodeLength
        };

        return new EnrichedInstruction(customInstruction) {
            Bytes = memory.ReadRam(CallbackOpcodeLength, address.Linear),
            Function = functions.SingleOrDefault(pair => pair.Key == address).Value,
            SegmentedAddress = address,
            Breakpoints = breakpointsViewModel.GetExecutionBreakPointsAtAddress(address.Linear).ToImmutableList(),
            InstructionFormatOverride = [
                new FormattedTextSegment {
                    Text = "Spice86 callback ",
                    Kind = FormatterTextKind.Directive
                },
                new FormattedTextSegment {
                    Text = callbackIndex.ToString().PadLeft(3),
                    Kind = FormatterTextKind.Function
                }
            ]
        };
    }

    /// <summary>
    /// Creates a standard enriched instruction from a decoded instruction at the given address.
    /// </summary>
    private EnrichedInstruction CreateStandardInstruction(Instruction instruction, SegmentedAddress address) {
        return new EnrichedInstruction(instruction) {
            Bytes = memory.ReadRam((uint)instruction.Length, address.Linear),
            Function = functions.SingleOrDefault(pair => pair.Key == address).Value,
            SegmentedAddress = address,
            Breakpoints = breakpointsViewModel.GetExecutionBreakPointsAtAddress(address.Linear).ToImmutableList(),
        };
    }

    /// <summary>
    /// Decodes instructions before the center address, stopping if we would overlap with the center address.
    /// </summary>
    private void DecodeInstructionsBefore(byte[] memoryBlock, int centerOffset, SegmentedAddress startAddress, SegmentedAddress centerAddress, Dictionary<uint, EnrichedInstruction> instructions) {
        if (centerOffset <= 0) {
            return;
        }

        var codeReader = new ByteArrayCodeReader(memoryBlock, 0, centerOffset);
        var decoder = Decoder.Create(16, codeReader, X86DecoderOptions);
        decoder.IP = startAddress.Offset;
        SegmentedAddress currentAddress = startAddress;

        // Decode instructions before the center address
        while (codeReader.Position < centerOffset && currentAddress.Linear < centerAddress.Linear) {
            int currentOffset = codeReader.Position;

            if (IsCallbackOpcode(memoryBlock, currentOffset, out ushort callbackIndex)) {
                instructions[currentAddress.Linear] = CreateCallbackOpcodeInstruction(currentAddress, callbackIndex);
                codeReader.Position += CallbackOpcodeLength;
                currentAddress += CallbackOpcodeLength;
            } else {
                decoder.Decode(out Instruction instruction);
                // Check if this instruction would overlap with the center address
                if (currentAddress.Linear + instruction.Length > centerAddress.Linear) {
                    break;
                }
                instructions[currentAddress.Linear] = CreateStandardInstruction(instruction, currentAddress);
                currentAddress += (ushort)instruction.Length;
            }
        }
    }

    /// <summary>
    /// Decodes instructions starting from a specific offset in the memory block.
    /// </summary>
    private void DecodeInstructionsFrom(byte[] memoryBlock, int startOffset, int maxLength, SegmentedAddress startingAddress, Dictionary<uint, EnrichedInstruction> instructions) {
        if (startOffset >= memoryBlock.Length || maxLength <= 0) {
            return;
        }

        var codeReader = new ByteArrayCodeReader(memoryBlock, startOffset, maxLength);
        var decoder = Decoder.Create(16, codeReader, X86DecoderOptions);
        decoder.IP = startingAddress.Offset;
        SegmentedAddress currentAddress = startingAddress;

        // Decode instructions from the starting address
        while (codeReader.Position < maxLength && startOffset + codeReader.Position < memoryBlock.Length) {
            int currentOffset = startOffset + codeReader.Position;

            if (IsCallbackOpcode(memoryBlock, currentOffset, out ushort callbackIndex)) {
                instructions[currentAddress.Linear] = CreateCallbackOpcodeInstruction(currentAddress, callbackIndex);
                codeReader.Position += CallbackOpcodeLength;
                currentAddress += CallbackOpcodeLength;
            } else {
                decoder.Decode(out Instruction instruction);
                instructions[currentAddress.Linear] = CreateStandardInstruction(instruction, currentAddress);
                currentAddress += (ushort)instruction.Length;
            }
        }
    }
}