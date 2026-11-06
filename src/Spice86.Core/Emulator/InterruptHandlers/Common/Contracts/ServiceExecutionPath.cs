namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// How a typed service helper executes its declared service.
/// </summary>
public enum ServiceExecutionPath {
    /// <summary>The helper performs the interrupt call: it runs the service's stub bytes,
    /// which dispatch to the C# callback.</summary>
    Stub,

    /// <summary>The helper invokes the service's State-independent implementation directly
    /// inside an <c>int</c>-equivalent frame, without running the stub bytes.</summary>
    Direct
}
