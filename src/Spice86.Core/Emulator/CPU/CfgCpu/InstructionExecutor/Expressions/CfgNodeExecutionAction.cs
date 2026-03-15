namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

public delegate void CfgNodeExecutionAction<in TInstructionExecutionHelper>(TInstructionExecutionHelper helper);
