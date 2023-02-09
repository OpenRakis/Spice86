using Spice86.Logging;

namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.DI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;

using System;

public class MachineCreator {
    public ProgramExecutor CreateProgramExecutorFromBinName(string binName) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin");
    }

    public ProgramExecutor CreateProgramExecutorForBin(string binPath) {
        Configuration configuration = new Configuration {
            // making sure int8 is not going to be triggered during the tests
            InstructionsPerSecond = 10000000,
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            InitializeDOS = false
        };

        ProgramExecutor programExecutor = new ProgramExecutor(
            new ServiceProvider().GetService<ILoggerService>(),
            null, null, configuration);;
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