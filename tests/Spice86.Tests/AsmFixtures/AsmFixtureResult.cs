namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// What an oracle may read after a run: memory and CPU state. An <c>AssertResult</c> may only read what the
/// emulated run and the generated run must agree on: memory, general and segment registers, <c>SP</c>, flags,
/// and values the program wrote to I/O ports, captured by a handler that the fixture installs in
/// <c>ConfigureMachine</c>. It must not read <c>CS:IP</c> after the end, <c>IsRunning</c>,
/// <c>InterruptShadowing</c>, cycle counts, or any CFG node or block: those are engine checks and stay in
/// <c>MachineTest</c>.
/// </summary>
/// <param name="Memory">Memory of the finished run.</param>
/// <param name="State">CPU state of the finished run.</param>
internal sealed record AsmFixtureResult(IMemory Memory, State State);
