namespace Spice86.Infrastructure;

using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

public interface IProgramExecutorFactory {
    IProgramExecutor Create(IGui? gui = null);
}