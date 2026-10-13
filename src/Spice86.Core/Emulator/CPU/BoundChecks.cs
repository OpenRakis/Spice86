namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>Checks the signed inclusive bounds used by the BOUND instruction.</summary>
public static class BoundChecks {
    /// <summary>Throws a CPU bounds fault when the index is outside the inclusive range.</summary>
    /// <param name="index">The signed index.</param>
    /// <param name="lower">The inclusive lower bound.</param>
    /// <param name="upper">The inclusive upper bound.</param>
    /// <exception cref="CpuBoundRangeExceededException">The index is outside the bounds.</exception>
    public static void Check(int index, int lower, int upper) {
        if (index < lower || index > upper) {
            throw new CpuBoundRangeExceededException(
                $"BOUND check failed: index={index}, lower={lower}, upper={upper}.");
        }
    }
}
