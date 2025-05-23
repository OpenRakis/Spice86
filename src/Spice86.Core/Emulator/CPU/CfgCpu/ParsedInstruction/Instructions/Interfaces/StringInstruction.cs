﻿namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

public interface StringInstruction {
    public void ExecuteStringOperation(InstructionExecutionHelper helper);
    
    /// <summary>
    /// Whether this String instruction can modify CPU flags or not
    /// </summary>
    public bool ChangesFlags { get; }
    public RepPrefix? RepPrefix { get; }
}