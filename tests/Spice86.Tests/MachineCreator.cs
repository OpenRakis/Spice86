namespace Spice86.Tests;

using System;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

public class MachineCreator {
    public ProgramExecutor CreateProgramExecutorFromBinName(string binName, bool enablePit, bool recordData) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin", enablePit, recordData);
    }

    public ProgramExecutor CreateProgramExecutorForBin(string binPath, bool enablePit, bool recordData) {
        Configuration configuration = new Configuration {
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            InitializeDOS = false,
            DumpDataOnExit = recordData,
            TimeMultiplier = enablePit ? 1 : 0,
            // Use instructions per second based timer for predictability if timer is enabled
            InstructionsPerSecond = enablePit ? 100000 : null
        };

        ILoggerService loggerService = Substitute.For<LoggerService>(new LoggerPropertyBag());
        ProgramExecutor programExecutor = new ProgramExecutor(configuration, loggerService, null, new PauseHandler(loggerService));
        Machine machine = programExecutor.Machine;
        Cpu cpu = machine.Cpu;
        cpu.ErrorOnUninitializedInterruptHandler = false;
        State state = machine.CpuState;
        state.Flags.IsDOSBoxCompatible = false;
        return programExecutor;
    }
}