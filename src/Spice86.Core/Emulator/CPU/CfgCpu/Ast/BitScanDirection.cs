namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>The scan direction of a bit scan helper call (BSF / BSR).</summary>
public enum BitScanDirection {
    /// <summary>Scan from the least significant bit up (BSF).</summary>
    Forward,
    /// <summary>Scan from the most significant bit down (BSR).</summary>
    Reverse
}
