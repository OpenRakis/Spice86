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
public class CurrentInstructions : InstructionReplacer {
    private readonly IMemory _memory;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    /// <summary>
    /// Instruction currently known to be in memory at a given address.
    /// Memory write breakpoints invalidate this cache when CPU writes there.
    /// </summary>
    private readonly Dictionary<SegmentedAddress, CfgInstruction> _currentInstructionAtAddress = new();


    /// <summary>
    /// Breakpoints that have been installed to monitor instruction at a given address. So that we can reset them when we want.
    /// </summary>
    private readonly Dictionary<SegmentedAddress, List<AddressBreakPoint>> _breakpointsForInstruction = new();

    public CurrentInstructions(IMemory memory, EmulatorBreakpointsManager emulatorBreakpointsManager,
        InstructionReplacerRegistry replacerRegistry) : base(replacerRegistry) {
        _memory = memory;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }

    public IEnumerable<CfgInstruction> GetAll() {
        return _currentInstructionAtAddress.Values;
    }

    public CfgInstruction? GetAtAddress(SegmentedAddress address) {
        _currentInstructionAtAddress.TryGetValue(address, out CfgInstruction? res);
        return res;
    }

    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        SegmentedAddress instructionAddress = newInstruction.Address;
        if (_currentInstructionAtAddress.ContainsKey(instructionAddress)) {
            ClearCurrentInstruction(oldInstruction);
            SetAsCurrent(newInstruction);
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
        foreach (FieldWithValue field in instruction.FieldsInOrder) {
            for (int i = 0; i < field.DiscriminatorValue.Count; i++) {
                byte? instructionByte = field.DiscriminatorValue[i];
                if (instructionByte is null) {
                    // Do not create breakpoints for fields that are not read from memory
                    continue;
                }

                uint byteAddress = (uint)(field.PhysicalAddress + i);
                AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.MEMORY_WRITE, byteAddress,
                    b => { OnBreakPointReached((AddressBreakPoint)b, instruction); }, false);
                breakpoints.Add(breakPoint);
                _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
            }
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
        instruction.SetLive(true);
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
        instruction.SetLive(false);
    }
}