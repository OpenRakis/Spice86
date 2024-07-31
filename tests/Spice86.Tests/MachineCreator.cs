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
    public ProgramExecutor CreateProgramExecutorFromBinName(string binName, bool recordData = false) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin", recordData);
    }

    public ProgramExecutor CreateProgramExecutorForBin(string binPath, bool recordData = false) {
        Configuration configuration = new Configuration {
            // making sure int8 is not going to be triggered during the tests
            InstructionsPerSecond = 10000000,
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            InitializeDOS = false,
            DumpDataOnExit = recordData
        };

        ILoggerService loggerService = Substitute.For<LoggerService>(new LoggerPropertyBag());
        ProgramExecutor programExecutor = new ProgramExecutor(configuration, loggerService, new PauseHandler(loggerService), null);
        Machine machine = programExecutor.Machine;
        Cpu cpu = machine.Cpu;
        cpu.ErrorOnUninitializedInterruptHandler = false;
        State state = machine.CpuState;
        state.Flags.IsDOSBoxCompatible = false;
        return programExecutor;
    }
}