using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.LoadableFile.Dos;

public abstract class DosFileLoader : ExecutableFileLoader {
    protected DosFileLoader(Machine machine) : base(machine) {
    }

    public override bool DosInitializationNeeded => true;
}