namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Interface for ASM user routine storage
/// </summary>
public interface IAsmUserRoutineHandler {
    /// <summary>
    /// Sets the address of the user ASM routine to call.
    /// </summary>
    /// <param name="segment">Segment of the user routine.</param>
    /// <param name="offset">Offset of the user routine.</param>
    public void SetUserRoutineAddress(ushort segment, ushort offset);

    /// <summary>
    /// Insets anything previously set as user routine and means no user routine.
    /// </summary>
    public void DisableUserRoutine();
}