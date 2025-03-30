namespace Spice86.MemoryWrappers;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
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
internal class InstructionsDecoder {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IDictionary<SegmentedAddress, FunctionInformation> _functions;
    private readonly BreakpointsViewModel _breakpointsViewModel;

    public InstructionsDecoder(IMemory memory, State state, IDictionary<SegmentedAddress, FunctionInformation> functions, BreakpointsViewModel breakpointsViewModel) {
        _memory = memory;
        _state = state;
        _functions = functions;
        _breakpointsViewModel = breakpointsViewModel;
    }

    /// <summary>
    /// Decodes instructions starting from the specified address.
    /// </summary>
    /// <param name="startAddress">The address to start decoding from</param>
    /// <param name="numberOfInstructionsShown">The number of instructions to decode</param>
    /// <returns>A list of decoded CPU instructions</returns>
    public List<CpuInstructionInfo> DecodeInstructions(uint startAddress, int numberOfInstructionsShown) {
        CodeReader codeReader = CreateCodeReader(_memory, out CodeMemoryStream emulatedMemoryStream);
        using CodeMemoryStream codeMemoryStream = emulatedMemoryStream;
        Decoder decoder = InitializeDecoder(codeReader, startAddress);
        int byteOffset = 0;
        codeMemoryStream.Position = startAddress;
        var instructions = new List<CpuInstructionInfo>();
        while (instructions.Count < numberOfInstructionsShown) {
            long instructionAddress = codeMemoryStream.Position;
            if (instructionAddress >= emulatedMemoryStream.Length) {
                break;
            }
            decoder.Decode(out Instruction instruction);
            CpuInstructionInfo instructionInfo = new() {
                Instruction = instruction,
                Address = (uint)instructionAddress,
                AddressInformation = $"{instructionAddress} (0x{_state.CS:x4}:{(ushort)(_state.IP + byteOffset):X4})",
                Length = instruction.Length,
                IP16 = instruction.IP16,
                IP32 = instruction.IP32,
                MemorySegment = instruction.MemorySegment,
                SegmentPrefix = instruction.SegmentPrefix,
                IsStackInstruction = instruction.IsStackInstruction,
                IsIPRelativeMemoryOperand = instruction.IsIPRelativeMemoryOperand,
                IPRelativeMemoryAddress = instruction.IPRelativeMemoryAddress,
                FlowControl = instruction.FlowControl,
                Bytes = $"""{Convert.ToHexString(_memory.GetData(
                    (uint)instructionAddress,
                    (uint)instruction.Length))} ({instruction.Length})"""
            };
            KeyValuePair<SegmentedAddress, FunctionInformation> functionInformation = _functions.FirstOrDefault(x => x.Key.Linear == instructionAddress);
            if (functionInformation.Key != default) {
                instructionInfo.FunctionName = functionInformation.Value.Name;
            }
            instructionInfo.SegmentedAddress = new(_state.CS, (ushort)(_state.IP + byteOffset));
            instructionInfo.Breakpoint = _breakpointsViewModel.GetBreakpoint(instructionInfo);
            instructionInfo.StringRepresentation = $"{instructionInfo.Address:X4} ({instructionInfo.SegmentedAddress}): {instruction} ({instructionInfo.Bytes})";
            if (instructionAddress == _state.IpPhysicalAddress) {
                instructionInfo.IsCsIp = true;
            }

            instructions.Add(instructionInfo);
            byteOffset += instruction.Length;
        }

        return instructions;
    }

    /// <summary>
    /// Enhanced decoder that supports bidirectional decoding and custom formatting for modern views.
    /// </summary>
    /// <param name="centerAddress">The address to center the decoding around</param>
    /// <param name="blockSize">The size of the memory block around the requested address to decode</param>
    /// <returns>A list of decoded CPU instructions</returns>
    public Dictionary<uint, EnrichedInstruction> DecodeInstructionsExtended(SegmentedAddress centerAddress, uint blockSize) {
        uint halfBlockSize = blockSize / 2;

        // Let's always start at the start of the segment
        uint blockStartAddress = (uint)(centerAddress.Segment << 4);
        uint length = Math.Min(Math.Max(blockSize, centerAddress.Offset + halfBlockSize), 0xFFFF);

        // Read the memory block
        byte[] memoryBlock = _memory.ReadRam(length, blockStartAddress);

        // Create a decoder for the memory block
        var codeReader = new ByteArrayCodeReader(memoryBlock);
        var decoder = Decoder.Create(16, codeReader);

        // Create a dictionary to hold the instructions
        var instructions = new Dictionary<uint, EnrichedInstruction>();

        var currentAddress = new SegmentedAddress(centerAddress.Segment, 0);
        decoder.IP = 0;
        while (currentAddress.Offset < (uint)memoryBlock.Length) {
            // Decode the instruction
            decoder.Decode(out Instruction instruction);

            // Create instruction info
            EnrichedInstruction enrichedInstruction = new(instruction) {
                Bytes = _memory.ReadRam((uint)instruction.Length, currentAddress.Linear),
                Function = _functions.SingleOrDefault(pair => pair.Key.Linear == currentAddress.Linear).Value,
                SegmentedAddress = currentAddress,
                Breakpoints = _breakpointsViewModel.Breakpoints.Where(bp => bp.Address == currentAddress.Linear && bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS).ToList()
            };

            // Add to our collection
            instructions[currentAddress.Linear] = enrichedInstruction;

            // Move to the next instruction
            currentAddress += (ushort)instruction.Length;
        }

        return instructions;
    }

    /// <summary>
    /// Initializes a decoder with the specified code reader and start address.
    /// </summary>
    /// <param name="codeReader">The code reader to use</param>
    /// <param name="startAddress">The address to start decoding from</param>
    /// <returns>An initialized decoder</returns>
    private static Decoder InitializeDecoder(CodeReader codeReader, uint startAddress) {
        Decoder decoder = Decoder.Create(16, codeReader, startAddress, DecoderOptions.Loadall286 | DecoderOptions.Loadall386);

        return decoder;
    }

    /// <summary>
    /// Creates a code reader for the classic view.
    /// </summary>
    /// <param name="memory">The memory to read from</param>
    /// <param name="emulatedMemoryStream">The memory stream to use</param>
    /// <returns>A code reader for the classic view</returns>
    private static CodeReader CreateCodeReader(IMemory memory, out CodeMemoryStream emulatedMemoryStream) {
        emulatedMemoryStream = new CodeMemoryStream(memory);

        return new StreamCodeReader(emulatedMemoryStream);
    }
}