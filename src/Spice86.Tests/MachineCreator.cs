namespace Spice86.Tests;

using Emulator;
using Emulator.CPU;
using Emulator.VM;

using System;

public class MachineCreator {
    public ProgramExecutor CreateProgramExecutorFromBinName(string binName) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin");
    }

    public ProgramExecutor CreateProgramExecutorForBin(string binPath) {
        Configuration configuration = new Configuration() {
            CreateAudioBackend = false
        };
        // making sure int8 is not going to be triggered during the tests
        configuration.InstructionsPerSecond = 10000000;
        configuration.Exe = binPath;
        // Don't expect any hash for the exe
        configuration.ExpectedChecksumValue = Array.Empty<byte>();
        configuration.InstallInterruptVector = false;

        ProgramExecutor programExecutor = new ProgramExecutor(null, configuration);
        Machine machine = programExecutor.Machine;
        Cpu cpu = machine.Cpu;
        // Disabling custom IO handling
        cpu.IoPortDispatcher = null;
        cpu.ErrorOnUninitializedInterruptHandler = false;
        State state = cpu.State;
        state.Flags.IsDOSBoxCompatible = false;
        return programExecutor;
    }
}