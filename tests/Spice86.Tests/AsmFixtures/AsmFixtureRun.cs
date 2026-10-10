namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.VM;

/// <summary>One finished fixture run: the emulator graph, the machine and the result an oracle may read.</summary>
internal sealed class AsmFixtureRun : IDisposable {
    private readonly Spice86Creator _creator;

    /// <summary>Builds the emulator for the given creator. The program is not run here.</summary>
    /// <param name="creator">Creator that builds the emulator to run the fixture.</param>
    public AsmFixtureRun(Spice86Creator creator) {
        _creator = creator;
        DependencyInjection = creator.Create();
        Machine = DependencyInjection.Machine;
        Result = new AsmFixtureResult(Machine.Memory, Machine.CpuState);
    }

    /// <summary>The emulator graph of the run.</summary>
    public Spice86DependencyInjection DependencyInjection { get; }

    /// <summary>The machine the program runs on.</summary>
    public Machine Machine { get; }

    /// <summary>The result an oracle may read.</summary>
    public AsmFixtureResult Result { get; }

    /// <summary>Disposes the emulator graph, then the creator.</summary>
    public void Dispose() {
        DependencyInjection.Dispose();
        _creator.Dispose();
    }
}
