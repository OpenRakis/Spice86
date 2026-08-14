namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>Protected-mode helper operations carried explicitly by execution AST calls.</summary>
public enum InstructionExecutionHelperOperation {
    /// <summary>Checks the current I/O privilege level.</summary>
    EnsureIoPrivilege,
    /// <summary>Loads a segment register and its descriptor cache.</summary>
    LoadSegmentRegister,
    /// <summary>Validates a far code-segment transfer.</summary>
    ValidateFarCodeSegmentTransfer,
    /// <summary>Reads a control register.</summary>
    ReadControlRegister,
    /// <summary>Writes a control register.</summary>
    WriteControlRegister,
    /// <summary>Reads the machine status word.</summary>
    ReadMachineStatusWord,
    /// <summary>Loads the machine status word.</summary>
    LoadMachineStatusWord,
    /// <summary>Clears the task-switched control bit.</summary>
    Clts,
    /// <summary>Loads the global descriptor table register.</summary>
    LoadGdtr,
    /// <summary>Stores the global descriptor table register.</summary>
    StoreGdtr,
    /// <summary>Loads the interrupt descriptor table register.</summary>
    LoadIdtr,
    /// <summary>Stores the interrupt descriptor table register.</summary>
    StoreIdtr,
    /// <summary>Loads the local descriptor table register.</summary>
    LoadLdtr,
    /// <summary>Stores the local descriptor table selector.</summary>
    StoreLdtr,
    /// <summary>Loads the task register.</summary>
    LoadTr,
    /// <summary>Stores the task selector.</summary>
    StoreTr,
    /// <summary>Tests whether a selector permits reading.</summary>
    VerifyReadable,
    /// <summary>Tests whether a selector permits writing.</summary>
    VerifyWritable,
    /// <summary>Tests whether LAR accepts a selector.</summary>
    IsSelectorValidForLar,
    /// <summary>Loads a selector's access rights.</summary>
    LoadAccessRights,
    /// <summary>Tests whether LSL accepts a selector.</summary>
    IsSelectorValidForLsl,
    /// <summary>Loads a selector's segment limit.</summary>
    LoadSegmentLimit,
    /// <summary>Tests whether ARPL must adjust the requested privilege level.</summary>
    WasPrivilegeLevelAdjusted,
    /// <summary>Adjusts a selector's requested privilege level.</summary>
    AdjustRequestedPrivilegeLevel,
    /// <summary>Pops a selector using the stack helper's operand-sized slot.</summary>
    PopSegmentSelector
}
