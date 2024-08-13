namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Cache of current instructions in memory.
/// Cache coherency is managed by breakpoints, as soon as an instruction is written in memory it is evicted.
/// </summary>
public class CurrentInstructions : IInstructionReplacer<CfgInstruction> {
    private readonly IMemory _memory;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    /// <summary>
    /// Instruction currently known to be in memory at a given address.
    /// Memory write breakpoints invalidate this cache when CPU writes there.
    /// </summary>
    private readonly Dictionary<SegmentedAddress, CfgInstruction> _currentInstructionAtAddress =
        new Dictionary<SegmentedAddress, CfgInstruction>();


    /// <summary>
    /// Breakpoints that have been installed to monitor instruction at a given address. So that we can reset them when we want.
    /// </summary>
    private readonly Dictionary<SegmentedAddress, List<AddressBreakPoint>> _breakpointsForInstruction =
        new Dictionary<SegmentedAddress, List<AddressBreakPoint>>();

    public CurrentInstructions(IMemory memory, EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _memory = memory;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }

    public CfgInstruction? GetAtAddress(SegmentedAddress address) {
        _currentInstructionAtAddress.TryGetValue(address, out CfgInstruction? res);
        return res;
    }

    public void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;
        if (_currentInstructionAtAddress.ContainsKey(instructionAddress)) {
            ClearCurrentInstruction(old);
            SetAsCurrent(instruction);
        }
    }


    public void SetAsCurrent(CfgInstruction instruction) {
        // Clear it because in some cases it can be added twice (discriminator reducer)
        ClearCurrentInstruction(instruction);
        // Set breakpoints so that we are notified if instruction changes in memory
        CreateBreakpointsForInstruction(instruction);
        // Add instruction in current cache
        AddInstructionInCurrentCache(instruction);
    }

    private void CreateBreakpointsForInstruction(CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;
        List<AddressBreakPoint> breakpoints = new();
        _breakpointsForInstruction.Add(instructionAddress, breakpoints);
        uint instructionPhysicalAddress = instructionAddress.ToPhysical();
        for (uint byteAddress = instructionPhysicalAddress;
             byteAddress < instructionPhysicalAddress + instruction.Length;
             byteAddress++) {
            // When reached the breakpoint will clear the cache and the other breakpoints for the instruction
            AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.WRITE, byteAddress,
                b => { OnBreakPointReached((AddressBreakPoint)b, instruction); }, false);
            breakpoints.Add(breakPoint);
            _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
        }
    }

    private void OnBreakPointReached(AddressBreakPoint addressBreakPoint, CfgInstruction instruction) {
        // Check that value is effectively being modified
        byte current = _memory.UInt8[addressBreakPoint.Address];
        byte newValue = _memory.CurrentlyWritingByte;
        if (current == newValue) {
            return;
        }
        ClearCurrentInstruction(instruction);
    }

    private void AddInstructionInCurrentCache(CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;
        _currentInstructionAtAddress.Add(instructionAddress, instruction);
    }

    private void ClearCurrentInstruction(CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;
        if (_breakpointsForInstruction.ContainsKey(instructionAddress)) {
            IList<AddressBreakPoint> breakpoints = _breakpointsForInstruction[instructionAddress];
            _breakpointsForInstruction.Remove(instructionAddress);
            foreach (AddressBreakPoint breakPoint in breakpoints) {
                _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, false);
            }
        }

        _currentInstructionAtAddress.Remove(instruction.Address);
    }
}