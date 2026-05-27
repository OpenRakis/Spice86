namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Shared.Emulator.Memory;

internal readonly record struct ContinuationTargetKey(ClassifiedEdgeKind Kind, int TargetBlockId, SegmentedAddress TargetAddress);