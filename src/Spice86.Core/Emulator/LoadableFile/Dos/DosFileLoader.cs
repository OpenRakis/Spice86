using Spice86.Core.DI;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

namespace Spice86.Core.Emulator.LoadableFile.Dos;

public abstract class DosFileLoader : ExecutableFileLoader {
    protected DosFileLoader(Machine machine) : base(machine,
        new ServiceProvider().GetService<ILoggerService>()) {
    }

    public override bool DosInitializationNeeded => true;
}