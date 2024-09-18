namespace Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// All the call conventions supported by the <see cref="Cpu"/>
/// </summary>
public enum CallType {
    /// <summary> For this call, only IP is on the stack </summary>
    NEAR,

    /// <summary> For this call, CS and IP are on the stack </summary>
    FAR,

    /// <summary> For this call, CS, IP, and the flags are on the stack </summary>
    INTERRUPT,

    /// <summary> Means called by the VM itself and not by emulated code. </summary>
    MACHINE,
}