namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;

public class ModRmHelper {
    public Memory Memory { get; private set; } = new(new(), new Ram(64), new());
    public InstructionFieldValueRetriever InstructionFieldValueRetriever { get; private set; }
    public State State { get; private set; } = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());

    public ModRmHelper() {
        InstructionFieldValueRetriever = new(Memory);
    }

    private void Init() {
        Memory = new(new(), new Ram(64), new A20Gate());
        InstructionFieldValueRetriever = new(Memory);
        State = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());
    }

    public byte GenerateModRm(int mod, int reg, int rm) {
        return (byte)((mod << 6) | (reg << 3) | rm);
    }

    public byte GenerateSib(int scale, int indexRegister, int baseRegister) {
        return (byte)((scale << 6) | (indexRegister << 3) | baseRegister);
    }

    public int WriteToMemory(params byte[] bytes) {
        Memory.LoadData(0, bytes, bytes.Length);
        return bytes.Length;
    }

    public ModRmParser CreateModRmParser() {
        Init();
        InstructionReader instructionReader = new(Memory);
        return new(instructionReader, State);
    }
    
    public (ModRmParser, ModRmExecutor) Create() {
        return (CreateModRmParser(), new ModRmExecutor(State, Memory, InstructionFieldValueRetriever));
    }

    public int BytesLength(List<FieldWithValue> fields) {
        return fields.Sum(field => field.Length);
    }
}