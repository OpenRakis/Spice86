using Spice86.Core.DI;
using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.LoadableFile.Dos;

public abstract class DosFileLoader : ExecutableFileLoader {
    protected DosFileLoader(Machine machine) : base(machine,
        new ServiceProvider().GetLoggerForContext<ExecutableFileLoader>()) {
    }

    public override bool DosInitializationNeeded => true;
}