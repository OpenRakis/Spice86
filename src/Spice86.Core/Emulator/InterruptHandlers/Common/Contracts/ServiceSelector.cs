namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Register an interrupt handler reads to select the service to run.
/// </summary>
public enum ServiceSelector {
    /// <summary>The service is selected by AH.</summary>
    Ah,
    /// <summary>The service is selected by AL.</summary>
    Al,
    /// <summary>The service is selected by the full AX value.</summary>
    Ax
}

/// <summary>Conversions between <see cref="ServiceSelector"/> and <see cref="CpuValues"/>.</summary>
public static class ServiceSelectorExtensions {
    /// <summary>The register bytes of the selector register.</summary>
    public static CpuValues ToCpuValues(this ServiceSelector selector) => selector switch {
        ServiceSelector.Ah => CpuValues.Ah,
        ServiceSelector.Al => CpuValues.Al,
        ServiceSelector.Ax => CpuValues.Ax,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, null)
    };
}
