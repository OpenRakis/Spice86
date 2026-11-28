namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// How a service hands control back when it runs: every DOS terminate service is declared so a
/// generated typed call knows whether the call site continues afterwards.
/// </summary>
public enum ServiceTransferKind {
    /// <summary>The service returns to the code after the <c>int</c>: the common case.</summary>
    Returns,
    /// <summary>The service runs a child program and returns to the caller when the child
    /// terminates (INT 21h AH=4Bh AL=00h EXEC).</summary>
    RunsChild,
    /// <summary>The service terminates the current process and never returns to the call site:
    /// control resumes at the terminate address of the parent process.</summary>
    Terminates,
    /// <summary>The service normally returns but can terminate the current process instead (the
    /// DOS services that check for Ctrl-C/Ctrl-Break).</summary>
    MayTerminate
}
