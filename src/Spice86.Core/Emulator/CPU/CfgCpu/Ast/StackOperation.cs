namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>The operation of a <c>Stack</c> helper call.</summary>
public enum StackOperation {
    /// <summary>Push one value.</summary>
    Push,
    /// <summary>Pop one value.</summary>
    Pop,
    /// <summary>Read a value without moving the stack pointer.</summary>
    Peek,
    /// <summary>The LEAVE stack frame teardown.</summary>
    Leave,
    /// <summary>Push the eight general registers (PUSHA / PUSHAD).</summary>
    PushAll,
    /// <summary>Pop the eight general registers (POPA / POPAD).</summary>
    PopAll
}
