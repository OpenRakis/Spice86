namespace Spice86.Core.Emulator.LoadableFile.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// An abstract class that represents a DOS executable file loader. It provides common functionality for loading executable files that conform to the DOS file format.
/// </summary>
public abstract class DosFileLoader : ExecutableFileLoader {

    /// <summary>
    /// Initializes a new instance of the <see cref="DosFileLoader"/> class with the specified <paramref name="machine"/> and <paramref name="loggerService"/>.
    /// </summary>
    /// <param name="loggerService">The <see cref="ILoggerService"/> instance.</param>
    protected DosFileLoader(IMemory memory, State state, ILoggerService loggerService) : base(memory, state, loggerService) {
    }

    /// <summary>
    /// Gets a value indicating whether the DOS initialization is needed for the loaded file.
    /// </summary>
    public override bool DosInitializationNeeded => true;
}