namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Options consumed by the emulation execution policy.
/// </summary>
/// <param name="Debug">When <c>true</c>, startup and shutdown breakpoints pause emulation.</param>
/// <param name="StopAfterCycles">Target cycle count used to register the stop-after-cycles breakpoint.</param>
/// <param name="GdbServer">Options used to configure the optional GDB server.</param>
public sealed record class ExecutionPolicyOptions(bool Debug, long StopAfterCycles, GdbServerOptions GdbServer);