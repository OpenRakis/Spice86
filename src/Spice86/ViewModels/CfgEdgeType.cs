namespace Spice86.ViewModels;

/// <summary>
/// Classifies CFG graph edges for automatic color assignment.
/// </summary>
public enum CfgEdgeType {
    Normal,
    Jump,
    Call,
    Return,
    Selector,
    CallToReturn,
    CpuFault
}
