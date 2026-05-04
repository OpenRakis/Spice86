namespace Spice86.Shared.Emulator.Dos;

/// <summary>
/// Provides the file name of the DOS program currently executing.
/// Written on the emulator thread, read on the UI thread.
/// </summary>
public interface ICurrentProcessNameProvider {
    /// <summary>
    /// Gets the file name (including extension) of the DOS program currently executing, or an empty string when at
    /// the root COMMAND.COM shell or when no DOS program is running.
    /// </summary>
    string CurrentProgramName { get; }
}
