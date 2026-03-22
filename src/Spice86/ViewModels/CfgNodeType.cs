namespace Spice86.ViewModels;

/// <summary>
/// Classifies CFG graph nodes by instruction type for visual styling.
/// </summary>
public enum CfgNodeType {
    Instruction,
    Jump,
    Call,
    Return,
    Selector
}
