namespace Spice86.Tests;

using System;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;

public class Spice86Creator {
    public Spice86DependencyInjection CreateSpice86ForBinName(string binName,  bool enablePit, bool recordData = false) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin", enablePit, recordData);
    }

    public Spice86DependencyInjection CreateProgramExecutorForBin(string binPath, bool enablePit, bool recordData = false) {
        Configuration configuration = new Configuration {
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            // making sure int8 is not going to be triggered during the tests
            InitializeDOS = false,
            DumpDataOnExit = recordData,
            TimeMultiplier = enablePit ? 1 : 0,
            //Don"t need nor want to instantiate the UI in emulator unit tests
            HeadlessMode = true,
            // Use instructions per second based timer for predictability if timer is enabled
            InstructionsPerSecond = enablePit ? 100000 : null
        };
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        Spice86DependencyInjection spice86DependencyInjection = new(loggerService, configuration);
        spice86DependencyInjection.Machine.Cpu.ErrorOnUninitializedInterruptHandler = false;
        spice86DependencyInjection.Machine.CpuState.Flags.IsDOSBoxCompatible = false;
        return spice86DependencyInjection;
    }
}