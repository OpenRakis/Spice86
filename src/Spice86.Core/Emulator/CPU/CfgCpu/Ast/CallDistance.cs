namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>Whether a call or return helper call transfers inside the code segment or across segments.</summary>
public enum CallDistance {
    /// <summary>Same-segment transfer (near call / near ret).</summary>
    Near,
    /// <summary>Cross-segment transfer (far call / far ret).</summary>
    Far
}
