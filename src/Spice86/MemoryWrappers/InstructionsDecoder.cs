namespace Spice86.MemoryWrappers;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Models.Debugging;
using Spice86.ViewModels;

using System;
using System.Collections.Generic;

internal class InstructionsDecoder {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IDictionary<uint, FunctionInformation> _functions;
    private readonly BreakpointsViewModel _breakpointsViewModel;

    public InstructionsDecoder(
        IMemory memory, State state, IDictionary<uint, FunctionInformation> functions, BreakpointsViewModel breakpointsViewModel) {
        _memory = memory;
        _state = state;
        _functions = functions;
        _breakpointsViewModel = breakpointsViewModel;
    }

    public List<CpuInstructionInfo> DecodeInstructions(uint startAddress,
        int numberOfInstructionsShown) {
        CodeReader codeReader = CreateCodeReader(_memory, out CodeMemoryStream emulatedMemoryStream);
        using CodeMemoryStream codeMemoryStream = emulatedMemoryStream;
        Decoder decoder = InitializeDecoder(codeReader, startAddress);
        int byteOffset = 0;
        codeMemoryStream.Position = startAddress;
        var instructions = new List<CpuInstructionInfo>();
        while (instructions.Count < numberOfInstructionsShown) {
            long instructionAddress = codeMemoryStream.Position;
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
                Bytes = $"""{Convert.ToHexString(_memory.GetData((uint)instructionAddress, (uint)instruction.Length))} ({instruction.Length})"""
            };
            if (_functions.TryGetValue((uint)instructionAddress, out FunctionInformation? functionInformation)) {
                instructionInfo.FunctionName = functionInformation.Name;
            }
            instructionInfo.SegmentedAddress = new(_state.CS, (ushort)(_state.IP + byteOffset));
            instructionInfo.Breakpoint = _breakpointsViewModel.GetBreakpoint(instructionInfo);
            instructionInfo.StringRepresentation =
                $"{instructionInfo.Address:X4} ({instructionInfo.SegmentedAddress}): {instruction} ({instructionInfo.Bytes})";
            if (instructionAddress == _state.IpPhysicalAddress) {
                instructionInfo.IsCsIp = true;
            }

            instructions.Add(instructionInfo);
            byteOffset += instruction.Length;
        }

        return instructions;
    }

    private static Decoder InitializeDecoder(CodeReader codeReader, uint currentIp) {
        Decoder decoder = Decoder.Create(16, codeReader, currentIp,
            DecoderOptions.Loadall286 | DecoderOptions.Loadall386);
        return decoder;
    }

    private static CodeReader CreateCodeReader(IMemory memory, out CodeMemoryStream codeMemoryStream) {
        codeMemoryStream = new CodeMemoryStream(memory);
        CodeReader codeReader = new StreamCodeReader(codeMemoryStream);
        return codeReader;
    }
}
