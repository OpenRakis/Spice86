using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.LoadableFile.Dos;

public abstract class DosFileLoader : ExecutableFileLoader {
    protected DosFileLoader(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
    }

    public override bool DosInitializationNeeded => true;
}