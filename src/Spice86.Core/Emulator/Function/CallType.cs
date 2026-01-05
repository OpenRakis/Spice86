namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// All the call conventions supported by the emulated CPU
/// </summary>
public enum CallType {
    /// <summary>For this call, only IP is on the stack.</summary>
    NEAR16,
    /// <summary>For this call, only EIP is on the stack.</summary>
    NEAR32,

    /// <summary>For this call, CS and IP are on the stack </summary>
    FAR16,
    /// <summary>For this call, CS and EIP are on the stack </summary>
    FAR32,

    /// <summary>For this call, CS, IP, and the flags are on the stack.</summary>
    INTERRUPT,

    /// <summary>Same as INTERRUPT but not triggered by an instruction.</summary>
    EXTERNAL_INTERRUPT,

    /// <summary> Means called by the VM itself and not by emulated code.</summary>
    MACHINE,
}