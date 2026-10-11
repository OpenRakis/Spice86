namespace Spice86.Tests.Function;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

using Xunit;
using Xunit.Sdk;

/// <summary>
/// Dispatch at the boundary where the interpreter hands control to an override, in both override modes.
/// A hand-written override is permissive by default: after its action the interpreter continues at
/// whatever CS:IP it left, including the override's own address, and it never causes another override
/// to run. A generated override registers with <see cref="FunctionInformation.ForbidsInterpreterFallback"/>:
/// its action must transfer control to another override, which is then dispatched iteratively at any
/// chain step including machine entry and the override's own address, or to the call's expected return
/// address, and any other destination fails explicitly instead of letting the interpreter execute the
/// code. In both modes stopping the machine ends the chain before anything else is dispatched, a
/// matching unwind completes the call, a non-matching or unmatched unwind propagates, and a cycling
/// chain of transfers runs to completion on one host stack frame.
/// </summary>
public class FunctionHandlerOverrideDispatchTest {
    private const int CyclingChainLength = 10000;

    [Fact]
    public void AnOverrideReturningToAnotherOverrideDispatchesTheTargetOverride() {
        // Arrange
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress transferTarget = Address(0x2000);
        SegmentedAddress expectedReturn = Address(0x0101);
        List<string> invoked = [];
        DefineOverride(context, callee, () => {
            invoked.Add("callee");
            Transfer(context.State, transferTarget);
        });
        DefineOverride(context, transferTarget, () => {
            invoked.Add("transferTarget");
            Transfer(context.State, expectedReturn);
        });

        // Act
        CallFromInterpreter(context, CallType.NEAR16, callee, expectedReturn);

        // Assert
        invoked.Should().Equal("callee", "transferTarget");
        context.State.IpSegmentedAddress.Should().Be(expectedReturn);
    }

    [Fact]
    public void ANestedCallReturningToAnOverriddenReturnAddressDispatchesTheOverrideThere() {
        // Arrange: an outer frame on the call stack, then a call whose return address is itself overridden
        TestContext context = CreateContext();
        SegmentedAddress outerEntry = Address(0x3000);
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress returnAddress = Address(0x1010);
        SegmentedAddress partitionAfterReturn = Address(0x1020);
        List<string> invoked = [];
        DefineOverride(context, callee, () => {
            invoked.Add("callee");
            Transfer(context.State, returnAddress);
        });
        DefineOverride(context, returnAddress, () => {
            invoked.Add("returnAddressPartition");
            Transfer(context.State, partitionAfterReturn);
        });
        DefineTerminatingOverride(context, partitionAfterReturn, () => {
            invoked.Add("terminalPartition");
            context.State.IsRunning = false;
        });
        context.FunctionHandler.Call(CallType.NEAR16, outerEntry, Address(0x3001), null, "outer");

        // Act
        CallFromInterpreter(context, CallType.NEAR16, callee, returnAddress);

        // Assert: the chain ends on the terminal override, which stopped the machine instead of handing back
        invoked.Should().Equal("callee", "returnAddressPartition", "terminalPartition");
        context.State.IpSegmentedAddress.Should().Be(partitionAfterReturn);
        context.State.IsRunning.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANonLocalReturnToAnAddressWithoutAnOverrideFailsExplicitly(bool registerTargetWithoutOverride) {
        // Arrange: the callee transfers control somewhere the call never expected to go
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress expectedReturn = Address(0x0101);
        SegmentedAddress unoverriddenTarget = Address(0x4000);
        DefineOverride(context, callee, () => Transfer(context.State, unoverriddenTarget));
        if (registerTargetWithoutOverride) {
            context.FunctionCatalogue.GetOrCreateFunctionInformation(unoverriddenTarget, "known_without_override");
        }

        // Act
        Action call = () => CallFromInterpreter(context, CallType.NEAR16, callee, expectedReturn);

        // Assert: never a silent hand-over to the interpreter
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unoverriddenTarget);
        exception.Message.Should().Contain("no C# override");
    }

    [Fact]
    public void AMachineEntryTransferToAnAddressWithoutAnOverrideFailsExplicitly() {
        // Arrange: the machine entry point has no expected return address, and its override transfers
        // control to an address with no override
        TestContext context = CreateContext();
        SegmentedAddress entry = Address(0xFFF0);
        SegmentedAddress unoverriddenTarget = Address(0x4000);
        DefineOverride(context, entry, () => Transfer(context.State, unoverriddenTarget));

        // Act
        Action call = () => CallFromInterpreter(context, CallType.MACHINE, entry, null);

        // Assert: the machine is still running at the unsupported destination when the failure fires
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unoverriddenTarget);
        exception.Message.Should().Contain("no C# override");
        context.State.IpSegmentedAddress.Should().Be(unoverriddenTarget);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AReturnChainLandingOnAnAddressWithoutAnOverrideFailsExplicitly() {
        // Arrange: an override chain whose last step transfers control to an address with no override
        TestContext context = CreateContext();
        SegmentedAddress first = Address(0x1000);
        SegmentedAddress second = Address(0x2000);
        SegmentedAddress unoverriddenTarget = Address(0x4000);
        List<string> invoked = [];
        DefineOverride(context, first, () => {
            invoked.Add("first");
            Transfer(context.State, second);
        });
        DefineOverride(context, second, () => {
            invoked.Add("second");
            Transfer(context.State, unoverriddenTarget);
        });

        // Act
        Action call = () => CallFromInterpreter(context, CallType.NEAR16, first, Address(0x0101));

        // Assert: both overrides ran, the machine is still running at the unsupported destination
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        invoked.Should().Equal("first", "second");
        exception.Target.Should().Be(unoverriddenTarget);
        exception.Message.Should().Contain("no C# override");
        context.State.IpSegmentedAddress.Should().Be(unoverriddenTarget);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AReturnChainThatCyclesBetweenTwoOverridesRunsToCompletion() {
        // Arrange: two overrides transferring control to each other until a fixed number of cycles ran
        TestContext context = CreateContext();
        SegmentedAddress first = Address(0x1000);
        SegmentedAddress second = Address(0x2000);
        SegmentedAddress end = Address(0x5000);
        int firstInvocations = 0;
        int endInvocations = 0;
        DefineOverride(context, first, () => {
            firstInvocations++;
            Transfer(context.State, second);
        });
        DefineOverride(context, second, () => {
            if (firstInvocations < CyclingChainLength) {
                Transfer(context.State, first);
            } else {
                Transfer(context.State, end);
            }
        });
        DefineTerminatingOverride(context, end, () => {
            endInvocations++;
            context.State.IsRunning = false;
        });

        // Act: iterative dispatch, so cycling stays on one host stack frame instead of recursing
        CallFromInterpreter(context, CallType.NEAR16, first, Address(0x0101));

        // Assert: the cycle ends by reaching a terminal override that stopped the machine
        firstInvocations.Should().Be(CyclingChainLength);
        endInvocations.Should().Be(1);
        context.State.IpSegmentedAddress.Should().Be(end);
        context.State.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void AMachineEntryOverrideTransferringToAnotherOverrideDispatchesIt() {
        // Arrange: the machine entry point has no expected return address
        TestContext context = CreateContext();
        SegmentedAddress entry = Address(0xFFF0);
        SegmentedAddress transferTarget = Address(0x0046);
        SegmentedAddress end = Address(0x1234);
        List<string> invoked = [];
        DefineOverride(context, entry, () => {
            invoked.Add("entry");
            Transfer(context.State, transferTarget);
        });
        DefineOverride(context, transferTarget, () => {
            invoked.Add("transferTarget");
            Transfer(context.State, end);
        });
        DefineTerminatingOverride(context, end, () => {
            invoked.Add("terminal");
            context.State.IsRunning = false;
        });

        // Act
        CallFromInterpreter(context, CallType.MACHINE, entry, null);

        // Assert
        invoked.Should().Equal("entry", "transferTarget", "terminal");
        context.State.IpSegmentedAddress.Should().Be(end);
        context.State.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void AHandWrittenOverrideEndingWithoutTransferringControlHandsBackToTheInterpreter() {
        // Arrange: a hand-written override that runs and ends without moving CS:IP
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        int invocations = 0;
        DefineHandWrittenOverride(context, callee, () => invocations++);

        // Act
        CallFromInterpreter(context, CallType.NEAR16, callee, Address(0x0101));

        // Assert: the interpreter continues where the override ran, once, with no failure
        invocations.Should().Be(1);
        context.State.IpSegmentedAddress.Should().Be(callee);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AHandWrittenOverrideTransferringToUnoverriddenCodeHandsBackToTheInterpreter() {
        // Arrange: a hand-written override that deliberately lets the interpreter run the code it does
        // not own, at an address that has no override
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress originalCode = Address(0x4000);
        int invocations = 0;
        DefineHandWrittenOverride(context, callee, () => {
            invocations++;
            Transfer(context.State, originalCode);
        });

        // Act
        CallFromInterpreter(context, CallType.NEAR16, callee, Address(0x0101));

        // Assert: the interpreter takes over at the transfer target, once, with no failure
        invocations.Should().Be(1);
        context.State.IpSegmentedAddress.Should().Be(originalCode);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AHandWrittenOverrideTransferringToItsOwnAddressHandsBackToTheInterpreter() {
        // Arrange: the old hand-written idiom of jumping back to the override's own address to mean
        // "now run the original assembly here"
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        int invocations = 0;
        DefineHandWrittenOverride(context, callee, () => {
            invocations++;
            Transfer(context.State, callee);
        });

        // Act
        CallFromInterpreter(context, CallType.NEAR16, callee, Address(0x0101));

        // Assert: the same-address transfer is not redispatched, the interpreter continues there
        invocations.Should().Be(1);
        context.State.IpSegmentedAddress.Should().Be(callee);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AHandWrittenOverrideTransferringToAnotherOverrideDoesNotDispatchIt() {
        // Arrange: a hand-written override transferring to an address that carries an override
        TestContext context = CreateContext();
        SegmentedAddress handWritten = Address(0x1000);
        SegmentedAddress registered = Address(0x2000);
        SegmentedAddress expectedReturn = Address(0x0101);
        List<string> invoked = [];
        DefineHandWrittenOverride(context, handWritten, () => {
            invoked.Add("handWritten");
            Transfer(context.State, registered);
        });
        DefineOverride(context, registered, () => {
            invoked.Add("registered");
            Transfer(context.State, expectedReturn);
        });

        // Act
        CallFromInterpreter(context, CallType.NEAR16, handWritten, expectedReturn);

        // Assert: only the hand-written activation ran, and the interpreter is left at its target
        invoked.Should().Equal("handWritten");
        context.State.IpSegmentedAddress.Should().Be(registered);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AStrictChainLandingOnAHandWrittenOverrideHandsBackAfterIt() {
        // Arrange: a strict chain whose last override is hand-written and transfers somewhere the call
        // never expected to return to
        TestContext context = CreateContext();
        SegmentedAddress strictEntry = Address(0x1000);
        SegmentedAddress handWritten = Address(0x2000);
        SegmentedAddress unregistered = Address(0x4000);
        List<string> invoked = [];
        DefineOverride(context, strictEntry, () => {
            invoked.Add("strict");
            Transfer(context.State, handWritten);
        });
        DefineHandWrittenOverride(context, handWritten, () => {
            invoked.Add("handWritten");
            Transfer(context.State, unregistered);
        });

        // Act
        CallFromInterpreter(context, CallType.NEAR16, strictEntry, Address(0x0101));

        // Assert: the mode of the activation that just ran decides, so the chain stops after the
        // hand-written override instead of failing on its destination
        invoked.Should().Equal("strict", "handWritten");
        context.State.IpSegmentedAddress.Should().Be(unregistered);
        context.State.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void AFiniteSameAddressReturnLoopRunsInGeneratedExecutionUntilItStopsItself() {
        // Arrange: an override whose near return keeps popping the address it is already at, the exact
        // shape a generated same-address return loop has
        TestContext context = CreateContext();
        SegmentedAddress loopAddress = Address(0x1000);
        ReturnOperationsHelper returns = new(context.State, context.Stack);
        const int IterationsBeforeStop = 4;
        int iterations = 0;
        context.FunctionCatalogue.FunctionInformations.Add(loopAddress,
            StrictFunction(loopAddress, "same_address_return_loop", _ => {
                iterations++;
                if (iterations >= IterationsBeforeStop) {
                    context.State.IsRunning = false;
                    return () => { };
                }
                context.Stack.Push16(loopAddress.Offset);
                return () => returns.NearRet16(0);
            }));

        // Act
        CallFromInterpreter(context, CallType.NEAR16, loopAddress, Address(0x0101));

        // Assert: every same-address return was dispatched again as generated code, the loop ended only
        // when it stopped the machine, and the stack stayed balanced
        iterations.Should().Be(IterationsBeforeStop);
        context.State.IsRunning.Should().BeFalse();
        context.State.IpSegmentedAddress.Should().Be(loopAddress);
        context.State.SP.Should().Be(0x0100);
    }

    [Fact]
    public void AFarReturnToTheAddressItStartedAtIsDispatchedAgainInsteadOfHandedBack() {
        // Arrange: a far return popping the CS:IP pair that is already there leaves the address
        // unchanged while still transferring control
        TestContext context = CreateContext();
        SegmentedAddress loopAddress = Address(0x1000);
        ReturnOperationsHelper returns = new(context.State, context.Stack);
        const int IterationsBeforeStop = 3;
        int iterations = 0;
        context.FunctionCatalogue.FunctionInformations.Add(loopAddress,
            StrictFunction(loopAddress, "same_address_far_return_loop", _ => {
                iterations++;
                if (iterations >= IterationsBeforeStop) {
                    context.State.IsRunning = false;
                    return () => { };
                }
                context.Stack.PushSegmentedAddress(loopAddress);
                return () => returns.FarRet16(0);
            }));

        // Act
        CallFromInterpreter(context, CallType.MACHINE, loopAddress, null);

        // Assert: the unchanged address was redispatched, the loop ended only by termination
        iterations.Should().Be(IterationsBeforeStop);
        context.State.IsRunning.Should().BeFalse();
        context.State.IpSegmentedAddress.Should().Be(loopAddress);
        context.State.SP.Should().Be(0x0100);
    }

    [Fact]
    public void AnInterruptReturnToTheAddressItStartedAtIsDispatchedAgainInsteadOfHandedBack() {
        // Arrange: an interrupt return popping the IP, CS and flags that lead back to the same address
        TestContext context = CreateContext();
        SegmentedAddress loopAddress = Address(0x1000);
        ReturnOperationsHelper returns = new(context.State, context.Stack);
        const int IterationsBeforeStop = 3;
        int iterations = 0;
        context.FunctionCatalogue.FunctionInformations.Add(loopAddress,
            StrictFunction(loopAddress, "same_address_interrupt_return_loop", _ => {
                iterations++;
                if (iterations >= IterationsBeforeStop) {
                    context.State.IsRunning = false;
                    return () => { };
                }
                context.Stack.Push16(context.State.Flags.FlagRegister16);
                context.Stack.PushSegmentedAddress(loopAddress);
                return () => returns.InterruptRet();
            }));

        // Act
        CallFromInterpreter(context, CallType.MACHINE, loopAddress, null);

        // Assert: the unchanged address was redispatched, the loop ended only by termination
        iterations.Should().Be(IterationsBeforeStop);
        context.State.IsRunning.Should().BeFalse();
        context.State.IpSegmentedAddress.Should().Be(loopAddress);
        context.State.SP.Should().Be(0x0100);
    }

    [Fact]
    public void AnOverrideBodyChangingTheDestinationIsRoutedByFinalCsIp() {
        // Arrange: an override whose body writes CS:IP directly while its declared transfer has a
        // no-operation deferred step, so routing must read the final destination instead of the step
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress unoverriddenTarget = Address(0x4000);
        context.FunctionCatalogue.FunctionInformations.Add(callee,
            StrictFunction(callee, "body_moves_cs_ip", _ => {
                Transfer(context.State, unoverriddenTarget);
                return () => { };
            }));

        // Act
        Action call = () => CallFromInterpreter(context, CallType.NEAR16, callee, Address(0x0101));

        // Assert: the body's transfer is validated like any other, never interpreted
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unoverriddenTarget);
        context.State.IpSegmentedAddress.Should().Be(unoverriddenTarget);
    }

    [Fact]
    public void AnOverrideTransferringControlToItsOwnAddressIsRedispatchedUntilItTerminates() {
        // Arrange: an override whose returned action leaves CS:IP and SP exactly as they are, declaring a
        // transfer to the address it is already at on every activation
        TestContext context = CreateContext();
        SegmentedAddress loopAddress = Address(0x1000);
        const int TransfersBeforeTermination = 3;
        int invocations = 0;
        List<int> hostStackDepths = [];
        context.FunctionCatalogue.FunctionInformations.Add(loopAddress,
            StrictFunction(loopAddress, "same_address_control_transfer", _ => {
                invocations++;
                hostStackDepths.Add(new StackTrace().FrameCount);
                if (invocations > TransfersBeforeTermination) {
                    context.State.IsRunning = false;
                }
                return () => { };
            }));

        // Act
        CallFromInterpreter(context, CallType.NEAR16, loopAddress, Address(0x0101));

        // Assert: four activations, the redispatches running iteratively on one host stack frame, ending
        // only when the machine stopped, with CS:IP and SP untouched and no hand-back to the interpreter
        invocations.Should().Be(TransfersBeforeTermination + 1);
        hostStackDepths.Skip(1).Should().OnlyContain(depth => depth == hostStackDepths[1],
            "every redispatch runs on the same host stack frame, so the chain does not grow the host stack");
        context.State.IsRunning.Should().BeFalse();
        context.State.IpSegmentedAddress.Should().Be(loopAddress);
        context.State.SP.Should().Be(0x0100);
    }

    [Fact]
    public void AnUnwindFromAnOverrideActionToAnotherAddressStillEscapes() {
        // Arrange: an override whose returned action unwinds to an address the call never expected to
        // return to, so the matching-unwind rule must not consume it
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress expectedReturn = Address(0x0101);
        SegmentedAddress unwindTarget = Address(0x4000);
        context.FunctionCatalogue.FunctionInformations.Add(callee,
            StrictFunction(callee, "unwinding", _ => () => {
                // A real unwind leaves the machine at its target before raising, as NearRet does when the
                // popped return address differs from the expected one.
                Transfer(context.State, unwindTarget);
                throw new CallStackUnwindException(unwindTarget, "unwind to an address that is not the expected return");
            }));

        // Act
        Action call = () => CallFromInterpreter(context, CallType.NEAR16, callee, expectedReturn);

        // Assert: the original target survives, nothing is translated into a normal return
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unwindTarget);
        context.State.IpSegmentedAddress.Should().Be(unwindTarget);
    }

    [Fact]
    public void AnUnwindFromAHandWrittenOverrideToAnotherAddressStillEscapes() {
        // Arrange: the hand-written twin of the escape above: a permissive override whose action unwinds
        // to an address the call never expected to return to
        TestContext context = CreateContext();
        SegmentedAddress callee = Address(0x1000);
        SegmentedAddress expectedReturn = Address(0x0101);
        SegmentedAddress unwindTarget = Address(0x4000);
        DefineHandWrittenOverride(context, callee, () => {
            // A real unwind leaves the machine at its target before raising, as NearRet does when the
            // popped return address differs from the expected one.
            Transfer(context.State, unwindTarget);
            throw new CallStackUnwindException(unwindTarget, "unwind to an address that is not the expected return");
        });

        // Act
        Action call = () => CallFromInterpreter(context, CallType.NEAR16, callee, expectedReturn);

        // Assert: the original target survives, nothing is translated into a normal return
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unwindTarget);
        context.State.IpSegmentedAddress.Should().Be(unwindTarget);
    }

    [Fact]
    public void AnUnwindFromAMachineEntryCallWithoutAnExpectedReturnStillEscapes() {
        // Arrange: the machine entry point defines no expected return address, so there is no continuation
        // an unwind could legitimately match
        TestContext context = CreateContext();
        SegmentedAddress entry = Address(0xFFF0);
        SegmentedAddress unwindTarget = Address(0x000D);
        context.FunctionCatalogue.FunctionInformations.Add(entry,
            StrictFunction(entry, "unwinding", _ => () => {
                // A real unwind leaves the machine at its target before raising, as NearRet does when the
                // popped return address differs from the expected one.
                Transfer(context.State, unwindTarget);
                throw new CallStackUnwindException(unwindTarget, "unwind with no expected return address to match");
            }));

        // Act
        Action call = () => CallFromInterpreter(context, CallType.MACHINE, entry, null);

        // Assert
        CallStackUnwindException exception = call.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(unwindTarget);
    }

    [Fact]
    public void AStoppedMachineIsNotDispatchedToAnotherOverrideAfterAnOverrideTransferredControl() {
        // Arrange: an override that transfers control to another override and stops the machine at once
        TestContext context = CreateContext();
        SegmentedAddress first = Address(0x1000);
        SegmentedAddress second = Address(0x2000);
        List<string> invoked = [];
        DefineTerminatingOverride(context, first, () => {
            invoked.Add("first");
            context.State.IsRunning = false;
            Transfer(context.State, second);
        });
        DefineOverride(context, second, () => invoked.Add("second"));

        // Act
        CallFromInterpreter(context, CallType.NEAR16, first, Address(0x0101));

        // Assert: termination was checked before dispatching, so the second override never ran
        invoked.Should().Equal("first");
        context.State.IsRunning.Should().BeFalse();
        context.State.IpSegmentedAddress.Should().Be(second);
    }

    private static void CallFromInterpreter(TestContext context, CallType callType, SegmentedAddress entry,
        SegmentedAddress? expectedReturn) {
        // The interpreter sets CS:IP to the call target before entering the function handler.
        context.State.CS = entry.Segment;
        context.State.IP = entry.Offset;
        context.FunctionHandler.Call(callType, entry, expectedReturn, null, "callee");
    }

    private static void DefineOverride(TestContext context, SegmentedAddress address, Action transfer) {
        context.FunctionCatalogue.FunctionInformations.Add(address,
            StrictFunction(address, "override", _ => () => transfer()));
    }

    private static void DefineTerminatingOverride(TestContext context, SegmentedAddress address, Action stop) {
        context.FunctionCatalogue.FunctionInformations.Add(address,
            StrictFunction(address, "terminating_override", _ => () => stop()));
    }

    /// <summary>
    /// Registers a hand-written override, which is permissive by default. A second dispatch of the same
    /// address would loop forever on a permissive hand-back, so it fails the test instead of hanging.
    /// </summary>
    private static void DefineHandWrittenOverride(TestContext context, SegmentedAddress address, Action transfer) {
        int dispatches = 0;
        context.FunctionCatalogue.FunctionInformations.Add(address,
            new FunctionInformation(address, "hand_written_override", _ => {
                dispatches++;
                if (dispatches > 1) {
                    throw new XunitException("redispatched");
                }
                return transfer;
            }));
    }

    /// <summary>Registers an override that owns the complete control flow of its code, like generated code.</summary>
    private static FunctionInformation StrictFunction(SegmentedAddress address, string name, Func<int, Action> overrideFunc) {
        return new FunctionInformation(address, name, overrideFunc) {
            ForbidsInterpreterFallback = true
        };
    }

    private static SegmentedAddress Address(ushort offset) {
        return new SegmentedAddress(0xF000, offset);
    }

    private static void Transfer(State state, SegmentedAddress target) {
        state.CS = target.Segment;
        state.IP = target.Offset;
    }

    private static TestContext CreateContext() {
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(), new RealModeMmu386(), false);
        State state = new(CpuModel.INTEL_80286) {
            CS = 0xF000,
            IP = 0x7777,
            SS = 0x3000,
            SP = 0x0100
        };
        Stack stack = new(memory, state);
        FunctionCatalogue functionCatalogue = new();
        ILogger loggerService = Substitute.For<ILogger>();
        FunctionHandler functionHandler = new(memory, state, functionCatalogue, true, loggerService);
        return new TestContext(memory, state, stack, functionCatalogue, functionHandler);
    }

    private sealed record TestContext(
        Memory Memory,
        State State,
        Stack Stack,
        FunctionCatalogue FunctionCatalogue,
        FunctionHandler FunctionHandler);
}
