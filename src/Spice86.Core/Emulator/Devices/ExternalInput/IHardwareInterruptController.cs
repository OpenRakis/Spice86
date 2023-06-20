namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Shared.Emulator.Errors;

public interface IHardwareInterruptController {
    /// <summary>
    /// Services an IRQ request
    /// </summary>
    /// <param name="irq">The IRQ Number, which will be internally translated to a vector number</param>
    /// <exception cref="UnrecoverableException">If not defined in the ISA bus IRQ table</exception>
    void InterruptRequest(byte irq);

    /// <summary>
    /// Acknowledges an interrupt by clearing the highest priority interrupt in-service bit.
    /// </summary>
    void AcknowledgeInterrupt();

    /// <summary>
    /// Determines if the interrupt controller has any pending interrupt requests.
    /// </summary>
    /// <returns>True if there is at least one pending interrupt request, false otherwise.</returns>
    bool HasPendingRequest();

    /// <summary>
    /// Processes the command byte write operation.
    /// </summary>
    /// <param name="value">The value to write.</param>
    void ProcessCommandWrite(byte value);

    /// <summary>
    /// Processes the command byte write operation.
    /// </summary>
    /// <param name="value">The value to write.</param>
    void ProcessDataWrite(byte value);

    /// <summary>
    /// Reads data from the highest priority ISR and clears the corresponding bit in the In-Service Register.
    /// </summary>
    /// <returns>The ISR value that was read.</returns>
    byte CommandRead();

    /// <summary>
    /// Reads a byte from the command register.
    /// </summary>
    /// <returns>The byte read from the command register.</returns>
    byte DataRead();
}