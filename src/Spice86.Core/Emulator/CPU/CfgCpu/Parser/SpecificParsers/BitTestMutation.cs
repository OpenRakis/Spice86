namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

/// <summary>Bit test mutation types for BT/BTS/BTR/BTC</summary>
public enum BitTestMutation {
    /// <summary>No mutation (BT: test only)</summary>
    None,
    /// <summary>Set the tested bit (BTS)</summary>
    Set,
    /// <summary>Reset the tested bit (BTR)</summary>
    Reset,
    /// <summary>Toggle the tested bit (BTC)</summary>
    Toggle
}
