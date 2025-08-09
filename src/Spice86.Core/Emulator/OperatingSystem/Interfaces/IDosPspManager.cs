namespace Spice86.Core.Emulator.OperatingSystem.Interfaces;

/// <summary>
/// Interface implemented by the DOS process manager class that creates and manages the PSP.
/// </summary>
/// <remarks>
/// This interface helps to decouple the DosProcessManager and DosMemoryManager classes. The only
/// thing that DosMemoryManager really needs to know from DosProcessManager is about the PSP before
/// the program that DosProcessManager loaded into memory so that it knows the start of the memory
/// that it can allocate.<br/><br/>
/// DosProcessManager is a much more complicated class with many more
/// functions and dependencies that DosMemoryManager doesn't care about, so decoupling them with
/// this interface that contains just the small subset of PSP-related functions that
/// DosMemoryManager needs makes it much easier to locally separate and mock for unit testing.
/// </remarks>
public interface IDosPspManager {
    /// <summary>
    /// Get the address of the PSP segment for the current program that is loaded.
    /// </summary>
    /// <returns>
    /// Return the PSP segment for the current program.
    /// </returns>
    public ushort GetCurrentPspSegment();
}