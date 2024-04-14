namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AdcAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AdcRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AdcRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AndAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AndRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AndRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CmpAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CmpRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CmpRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.DecReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp45;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.IncReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.OrAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.OrRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.OrRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PopReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushPopF;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SbbAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SbbRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SbbRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SubAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SubRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.SubRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.XorAccImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.XorRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.XorRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Executes the various instructions.
/// This also includes updating CS:IP.
/// After execution, Field NextNode is updated to reflect what was next according to the current graph.
/// If null it means the graph does not have a next node for this path.
/// It will be the job of the CfgCpu to link a new node accordingly.
/// </summary>
public class ExecutorCfgNodeVisitor : ICfgNodeVisitor {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly Stack _stack;
    private readonly IOPortDispatcher? _ioPortDispatcher;
    private readonly CallbackHandler _callbackHandler;
    private readonly InstructionFieldValueRetriever _instructionFieldValueRetriever;
    private readonly ModRmExecutor _modRm;
    private readonly Alu8 _alu8;
    private readonly Alu16 _alu16;
    private readonly Alu32 _alu32;

    private UInt16RegistersIndexer UInt16Registers => _state.GeneralRegisters.UInt16;
    private UInt32RegistersIndexer UInt32Registers => _state.GeneralRegisters.UInt32;

    public ExecutorCfgNodeVisitor(State state,
        IMemory memory,
        IOPortDispatcher? ioPortDispatcher,
        CallbackHandler callbackHandler) {
        _state = state;
        _memory = memory;
        _stack = new Stack(_memory, state);
        _alu8 = new(state);
        _alu16 = new(state);
        _alu32 = new(state);
        _ioPortDispatcher = ioPortDispatcher;
        _callbackHandler = callbackHandler;
        _instructionFieldValueRetriever = new(_memory);
        _modRm = new(state, memory, _instructionFieldValueRetriever);
    }

    public ICfgNode? NextNode { get; private set; }

    public void Accept(AddRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Add(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Add(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Add(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Add(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm8 instruction) {
        _state.AL = _alu8.Add(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm16 instruction) {
        _state.AX = _alu16.Add(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm32 instruction) {
        _state.EAX = _alu32.Add(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Or(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Or(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Or(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Or(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Or(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Or(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrAccImm8 instruction) {
        _state.AL = _alu8.Or(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrAccImm16 instruction) {
        _state.AX = _alu16.Or(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(OrAccImm32 instruction) {
        _state.EAX = _alu32.Or(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Adc(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Adc(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Adc(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Adc(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Adc(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Adc(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcAccImm8 instruction) {
        _state.AL = _alu8.Adc(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcAccImm16 instruction) {
        _state.AX = _alu16.Adc(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AdcAccImm32 instruction) {
        _state.EAX = _alu32.Adc(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sbb(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sbb(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sbb(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Sbb(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Sbb(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Sbb(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbAccImm8 instruction) {
        _state.AL = _alu8.Sbb(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbAccImm16 instruction) {
        _state.AX = _alu16.Sbb(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SbbAccImm32 instruction) {
        _state.EAX = _alu32.Sbb(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }
    public void Accept(AndRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.And(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.And(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.And(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.And(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.And(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.And(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndAccImm8 instruction) {
        _state.AL = _alu8.And(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndAccImm16 instruction) {
        _state.AX = _alu16.And(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AndAccImm32 instruction) {
        _state.EAX = _alu32.And(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }
    public void Accept(SubRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sub(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sub(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sub(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Sub(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Sub(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Sub(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubAccImm8 instruction) {
        _state.AL = _alu8.Sub(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubAccImm16 instruction) {
        _state.AX = _alu16.Sub(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(SubAccImm32 instruction) {
        _state.EAX = _alu32.Sub(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }
    
    public void Accept(XorRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Xor(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Xor(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Xor(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Xor(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Xor(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Xor(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorAccImm8 instruction) {
        _state.AL = _alu8.Xor(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorAccImm16 instruction) {
        _state.AX = _alu16.Xor(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(XorAccImm32 instruction) {
        _state.EAX = _alu32.Xor(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }
    
    public void Accept(CmpRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu8.Sub(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu8.Sub(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpAccImm8 instruction) {
        _alu8.Sub(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpAccImm16 instruction) {
        _alu16.Sub(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(CmpAccImm32 instruction) {
        _alu32.Sub(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(IncReg16 instruction) {
        UInt16Registers[instruction.RegisterIndex] = _alu16.Inc(UInt16Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(IncReg32 instruction) {
        UInt32Registers[instruction.RegisterIndex] = _alu32.Inc(UInt32Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(DecReg16 instruction) {
        UInt16Registers[instruction.RegisterIndex] = _alu16.Dec(UInt16Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(DecReg32 instruction) {
        UInt32Registers[instruction.RegisterIndex] = _alu32.Dec(UInt32Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PushReg16 instruction) {
        _stack.Push16(UInt16Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PushReg32 instruction) {
        _stack.Push32(UInt32Registers[instruction.RegisterIndex]);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PopReg16 instruction) {
        UInt16Registers[instruction.RegisterIndex] = _stack.Pop16();
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PopReg32 instruction) {
        UInt32Registers[instruction.RegisterIndex] = _stack.Pop32();
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Adc8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Adc(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Adc(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Adc(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Adc(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Adc(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Add8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Add(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1And8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.And(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.And(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.And(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.And(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.And(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Cmp8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu8.Sub(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Or8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Or(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Or(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Or(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Or(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Or(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Sbb8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sbb(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sbb(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sbb(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sbb(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sbb(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Sub8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sub(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sub(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sub(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sub(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sub(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Xor8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Xor(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Xor(_modRm.RM16,
            (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Xor(_modRm.RM32,
            (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Xor(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Xor(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _modRm.R8;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _modRm.R16;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _modRm.R32;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PushF16 instruction) {
        _stack.Push16((ushort)_state.Flags.FlagRegister);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PushF32 instruction) {
        _stack.Push32(_state.Flags.FlagRegister & 0x00FCFFFF);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PopF16 instruction) {
        _state.Flags.FlagRegister = _stack.Pop16();
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(PopF32 instruction) {
        _state.Flags.FlagRegister = _stack.Pop32();
    }

    public void Accept(MovMoffsAcc8 instruction) {
        _memory.UInt8[GetSegmentedAddress(instruction)] = _state.AL;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovMoffsAcc16 instruction) {
        _memory.UInt16[GetSegmentedAddress(instruction)] = _state.AX;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovMoffsAcc32 instruction) {
        _memory.UInt32[GetSegmentedAddress(instruction)] = _state.EAX;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm8 instruction) {
        byte value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt8HighLow[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm16 instruction) {
        ushort value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt16[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm32 instruction) {
        uint value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt32[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(JmpNearImm16 instruction) {
        short offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNearOffset(instruction, offset);
    }

    public void Accept(JmpNearImm8 instruction) {
        sbyte offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNearOffset(instruction, offset);
    }

    public void Accept(Hlt instruction) {
        _state.IsRunning = false;
        MoveIpToEndOfInstruction(instruction);
        NextNode = null;
    }

    public void Accept(Grp45RmInc8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Inc(_modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp45RmDec8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Dec(_modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp4Callback instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _callbackHandler.Run(_instructionFieldValueRetriever.GetFieldValue(instruction.CallbackNumber));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp45RmInc16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Inc(_modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp45RmInc32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Inc(_modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp45RmDec16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Dec(_modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp45RmDec32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Dec(_modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp5RmCallNear instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        ushort callAddress = _modRm.RM16;
        NearCallWithReturnIpNextInstruction(instruction, callAddress);
    }

    public void Accept(Grp5RmCallFar instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        uint? ipAddress = _modRm.MemoryAddress;
        if (ipAddress is null) {
            return;
        }
        (ushort cs, ushort ip) = _memory.SegmentedAddress[ipAddress.Value];
        FarCallWithReturnIpNextInstruction(instruction, cs, ip);
    }

    public void Accept(Grp5RmJumpNear instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        ushort ip = _modRm.RM16;
        JumpNear(instruction, ip);
    }

    public void Accept(Grp5RmJumpFar instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        uint? ipAddress = _modRm.MemoryAddress;
        if (ipAddress is null) {
            return;
        }
        (ushort cs, ushort ip) = _memory.SegmentedAddress[ipAddress.Value];
        JumpFar(instruction, cs, ip);
    }

    public void Accept(Grp5RmPush16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _stack.Push16(_modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp5RmPush32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _stack.Push32(_modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(DiscriminatedNode discriminatedNode) {
        int address = (int)discriminatedNode.Address.ToPhysical();
        foreach (Discriminator discriminator in discriminatedNode.SuccessorsPerDiscriminator.Keys) {
            int length = discriminator.DiscriminatorValue.Count;
            Span<byte> bytes = _memory.GetSpan(address, length);
            if (discriminator.SpanEquivalent(bytes)) {
                NextNode = discriminatedNode.SuccessorsPerDiscriminator[discriminator];
                return;
            }
        }

        NextNode = null;
    }

    private ushort SegmentValue(IInstructionWithSegmentRegisterIndex instruction) {
        return _state.SegmentRegisters.UInt16[instruction.SegmentRegisterIndex];
    }

    private ushort OffsetValue(IInstructionWithOffsetField instruction) {
        return _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
    }

    private SegmentedAddress GetSegmentedAddress(IInstructionWithSegmentRegisterIndexAndOffsetField instruction) {
        ushort segment = SegmentValue(instruction);
        ushort offset = OffsetValue(instruction);
        return new SegmentedAddress(segment, offset);
    }

    private void JumpNearOffset(CfgInstruction instruction, int offset) {
        MoveIpToEndOfInstruction(instruction);
        _state.IP = (ushort)(_state.IP + offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void JumpFar(CfgInstruction instruction, ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void JumpNear(CfgInstruction instruction, ushort ip) {
        _state.IP = ip;
        SetNextNodeToSuccessorAtCsIp(instruction);

    }

    public void NearCallWithReturnIpNextInstruction(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        NearCall(instruction, _state.IP, callIP);
    }

    private void NearCall(CfgInstruction instruction, ushort returnIP, ushort callIP) {
        _stack.Push16(returnIP);
        HandleCall(instruction, CallType.NEAR, _state.CS, returnIP, _state.CS, callIP);
    }

    public void FarCallWithReturnIpNextInstruction(CfgInstruction instruction, ushort targetCS, ushort targetIP) {
        MoveIpToEndOfInstruction(instruction);
        FarCall(instruction, _state.CS, _state.IP, targetCS, targetIP);
    }

    private void FarCall(CfgInstruction instruction,
        ushort returnCS,
        ushort returnIP,
        ushort targetCS,
        ushort targetIP) {
        _stack.Push16(returnCS);
        _stack.Push16(returnIP);
        HandleCall(instruction, CallType.FAR, returnCS, returnIP, targetCS, targetIP);
    }

    private void HandleCall(CfgInstruction instruction,
        CallType callType,
        ushort returnCS,
        ushort returnIP,
        ushort targetCS,
        ushort targetIP) {
        _state.CS = targetCS;
        // Setting it here as well for eventual overrides
        _state.IP = targetIP;
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    private void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        _state.IP = (ushort)(_state.IP + instruction.Length);
    }

    private ICfgNode? GetSuccessorAtCsIp(CfgInstruction instruction) {
        instruction.SuccessorsPerAddress.TryGetValue(_state.IpSegmentedAddress, out ICfgNode? res);
        return res;
    }

    private void SetNextNodeToSuccessorAtCsIp(CfgInstruction instruction) {
        NextNode = GetSuccessorAtCsIp(instruction);
    }

    private void MoveIpAndSetNextNode(CfgInstruction instruction) {
        MoveIpToEndOfInstruction(instruction);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }
}