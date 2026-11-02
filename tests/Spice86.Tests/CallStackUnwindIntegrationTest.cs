namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Interpreter-boundary reproduction of a non-local return: the interpreter executes <c>start</c> until
/// its CALL enters the override registered at <c>outer</c>, so the interpreter owns the CALL's own
/// continuation and no generated call helper does. The override reaches that continuation by discarding
/// the frame its nested call pushed, which raises a <see cref="CallStackUnwindException"/> whose target
/// only the interpreter-issued call can consume. The program body of <c>outer</c> must not run, so the
/// marker only reachable through it stays unwritten while the interpreter writes its own marker and
/// finishes the run with a balanced stack.
/// </summary>
public class CallStackUnwindIntegrationTest {
    private const ushort CodeSegment = 0xF000;
    private const ushort OuterOffset = 0x0100;
    private const ushort OuterContinuationOffset = 0x0103;
    private const ushort InterpreterContinuationOffset = 0x000D;
    private const ushort InterpreterMarkerAddress = 0x0010;
    private const ushort InnerReturnMarkerAddress = 0x0011;
    private const byte InterpreterMarker = 0xAA;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnInterpreterCallConsumesAnUnwindToItsExpectedReturn(bool invokeFromBody) {
        // Arrange
        AsmRunSettings settings = new() { BinName = "callstackunwind", MaxCycles = 10000 };
        OuterOverrideSupplier supplier = new(invokeFromBody);
        using Spice86Creator creator = settings.CreateOverrideCreator(supplier);
        using Spice86DependencyInjection dependencyInjection = creator.Create();

        // Act
        dependencyInjection.ProgramExecutor.Run();

        // Assert
        IMemory memory = dependencyInjection.Machine.Memory;
        memory.UInt8[0, InterpreterMarkerAddress].Should().Be(InterpreterMarker,
            "the interpreter must resume at the CALL continuation and run interpreter_return");
        memory.UInt8[0, InnerReturnMarkerAddress].Should().Be(0,
            "outer's assembly body must not run, so inner_return must never be reached");
        supplier.OuterInvocationCount.Should().Be(1, "the override at F000:0100 runs exactly once");
        dependencyInjection.Machine.CpuState.SP.Should().Be(0x0100,
            "the unwind must leave the stack exactly as the CALL found it");
    }

    /// <summary>Registers only the override at <c>F000:0100</c> and counts how often the run entered it.</summary>
    private sealed class OuterOverrideSupplier : IOverrideSupplier {
        private readonly bool _invokeFromBody;
        private int _outerInvocationCount;

        /// <summary>Creates the supplier.</summary>
        /// <param name="invokeFromBody">Whether the override body performs the unwind itself instead of
        /// returning it as the override's action.</param>
        public OuterOverrideSupplier(bool invokeFromBody) {
            _invokeFromBody = invokeFromBody;
        }

        /// <summary>How many times the run invoked the override registered at <c>F000:0100</c>.</summary>
        public int OuterInvocationCount => _outerInvocationCount;

        /// <inheritdoc />
        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartSegment, Machine machine) {
            OuterCallStackUnwindOverride helper = new(
                new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration,
                _invokeFromBody, CountOuterInvocation);
            return helper.FunctionInformations;
        }

        private void CountOuterInvocation() {
            _outerInvocationCount++;
        }
    }

    /// <summary>Replaces <c>outer</c> with a nested call whose callee removes the frame that call pushed,
    /// so the near return lands on the interpreter CALL's continuation instead.</summary>
    private sealed class OuterCallStackUnwindOverride : CSharpOverrideHelper {
        private readonly bool _invokeFromBody;
        private readonly Action _onOuterInvoked;

        /// <summary>Creates the helper and registers the single override it owns.</summary>
        /// <param name="functionInformations">Dictionary the registrations are added to.</param>
        /// <param name="machine">Emulator machine the override runs on.</param>
        /// <param name="loggerService">Logger service.</param>
        /// <param name="configuration">Emulator configuration.</param>
        /// <param name="invokeFromBody">Whether the override body performs the unwind itself instead of
        /// returning it as the override's action.</param>
        /// <param name="onOuterInvoked">Called once per invocation of the override body.</param>
        public OuterCallStackUnwindOverride(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
            Machine machine, ILogger loggerService, Configuration configuration, bool invokeFromBody,
            Action onOuterInvoked) : base(functionInformations, machine, loggerService, configuration) {
            _invokeFromBody = invokeFromBody;
            _onOuterInvoked = onOuterInvoked;
            DefineFunction(CodeSegment, OuterOffset, Outer, true, "callstackunwind_outer");
        }

        /// <summary>The override of <c>outer</c>.</summary>
        /// <param name="loadOffset">Unused load offset of the invocation.</param>
        /// <returns>The action the dispatcher must run, or a no-op when the body already unwound.</returns>
        public Action Outer(int loadOffset) {
            _onOuterInvoked();
            if (_invokeFromBody) {
                UnwindToInterpreterContinuation();
                return () => { };
            }
            return UnwindToInterpreterContinuation;
        }

        private void UnwindToInterpreterContinuation() {
            NearCall(CodeSegment, OuterContinuationOffset, _ => {
                Stack.Pop16();
                return NearRet();
            });
        }
    }
}
