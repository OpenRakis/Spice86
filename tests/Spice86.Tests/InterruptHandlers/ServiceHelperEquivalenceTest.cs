namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Reflection;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Proves every typed service helper does exactly what an <c>int</c> instruction to the
/// real handler stub does. For each contract case, machine A executes <c>int N</c> written at
/// a scratch address in the interpreter; machine B registers at the vector's target an
/// override that runs the same stub bytes in the interpreter and calls the helper
/// through reflection with the parameter values the case set up. After both runs, every
/// register, the FLAGS register and the whole memory must be identical.
/// </summary>
public class ServiceHelperEquivalenceTest {
    private const ushort ScratchSegment = 0x7000;
    private const ushort ScratchOffset = 0x0000;
    private const uint ScratchLinear = ScratchSegment * 16 + ScratchOffset;
    private const ushort ExpectedReturnIp = ScratchOffset + 2;
    private const int StepLimit = 100000;

    private readonly ILogger _logger = Substitute.For<ILogger>();

    /// <summary>One equivalence case per contract test case.</summary>
    public static IEnumerable<object[]> Cases =>
        ServiceContractCoverageTest.AllCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public void HelperCallEqualsRealInterrupt(ServiceContractCase testCase) {
        // Arrange / Act / Assert
        RunEquivalence(testCase, useLiteralOverload: false);
    }

    /// <summary>Runs the literal overload (<c>..., "HELLO"</c>) of each contract case whose service
    /// has one, with the literal read from <c>DS:DX</c> after the case setup; cases whose bytes
    /// would not print as a literal (no terminator within 64 bytes, an unprintable byte, an offset
    /// wrap) are skipped.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void LiteralHelperCallEqualsRealInterrupt(ServiceContractCase testCase) {
        // Arrange / Act / Assert
        RunEquivalence(testCase, useLiteralOverload: true);
    }

    /// <summary>Each literal overload runs at least one contract case whose <c>DS:DX</c> bytes
    /// satisfy the literal conditions.</summary>
    [Fact]
    public void EveryLiteralOverloadRunsAtLeastOneCase() {
        // Arrange / Act
        Dictionary<int, bool> covered = new();
        foreach (ServiceContractCase testCase in Cases.Select(c => (ServiceContractCase)c[0])) {
            using ServiceContractMachine machine = new(testCase.NeedsEms, testCase.NeedsXms);
            IInterruptHandler handler =
                machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[testCase.Vector];
            InterruptServiceEntry entry = ServiceContractHarness.EntryOf(handler,
                testCase.SelectorValue) ?? throw new InvalidOperationException(
                    $"no service entry declared for {testCase}");
            if (entry.StringArgument.Terminator() is not { } terminator) {
                continue;
            }
            covered.TryAdd(testCase.SelectorValue, false);
            testCase.Setup?.Invoke(machine);
            if (TryReadLiteral(machine.Spice86, terminator, out _)) {
                covered[testCase.SelectorValue] = true;
            }
        }

        // Assert
        foreach (int selectorValue in covered.Keys) {
            covered[selectorValue].Should().BeTrue(
                "no contract case of service 0x{0:X2} reads a printable terminated string at DS:DX",
                selectorValue);
        }
        covered.Should().HaveCount(9, "nine int 21h services have a string literal overload");
    }

    /// <summary>INT 15h AH=24h preserves outputs not written by the selected subfunction. The oracle pins pre-refactor register behavior before comparing the helper.</summary>
    [Theory]
    [InlineData(0x00, 0x2400, false)]
    [InlineData(0x01, 0x2401, false)]
    [InlineData(0x02, 0x0001, false)]
    [InlineData(0xFF, 0x24FF, true)]
    public void A20UnwrittenOutputsArePreserved(byte operation, ushort expectedAx,
        bool expectedCarry) {
        RunEquivalence(new ServiceContractCase {
            Vector = 0x15,
            SelectorValue = 0x24,
            Setup = machine => {
                State state = machine.Spice86.Machine.CpuState;
                state.AL = operation;
                state.BX = 0xBEEF;
                state.CarryFlag = true;
                machine.Spice86.Machine.Memory.A20Gate.IsEnabled = true;
            }
        }, false, stateA => {
            stateA.AX.Should().Be(expectedAx);
            stateA.BX.Should().Be(0xBEEF);
            stateA.CarryFlag.Should().Be(expectedCarry);
        });
    }

    /// <summary>INT 13h AH=04h (verify sectors): a nonfloppy success always writes
    /// AH=0, it does not preserve the selector.</summary>
    [Fact]
    public void VerifySectorsUnwrittenOutputsArePreserved() {
        RunEquivalence(new ServiceContractCase {
            Vector = 0x13,
            SelectorValue = 0x04,
            Setup = machine => {
                machine.Spice86.Machine.CpuState.AL = 1;
                machine.Spice86.Machine.CpuState.DX = 0x0180;
            }
        }, false, stateA => {
            stateA.AX.Should().Be(0x0001);
            stateA.CarryFlag.Should().BeFalse();
        });
    }

    /// <summary>INT 13h AH=15h (diskette/HDD type): CX and DX are outputs only for
    /// drive 0x80; other drives preserve them.</summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x81)]
    public void DisketteOrHddTypeUnwrittenOutputsArePreserved(byte drive) {
        ushort dx = (ushort)(0x5600 | drive);
        RunEquivalence(new ServiceContractCase {
            Vector = 0x13,
            SelectorValue = 0x15,
            Setup = machine => {
                machine.Spice86.Machine.CpuState.CX = 0x1234;
                machine.Spice86.Machine.CpuState.DX = dx;
            }
        }, false, stateA => {
            stateA.CX.Should().Be(0x1234);
            stateA.DX.Should().Be(dx);
        });
    }

    /// <summary>INT 67h AH=43h (EMS allocate pages): a failed allocation preserves
    /// DX instead of overwriting the handle output.</summary>
    [Theory]
    [InlineData(0x0000)]
    [InlineData(0xFFFF)]
    public void AllocatePagesUnwrittenOutputsArePreserved(ushort pageCount) {
        RunEquivalence(new ServiceContractCase {
            Vector = 0x67,
            SelectorValue = 0x43,
            NeedsEms = true,
            Setup = machine => {
                machine.Spice86.Machine.CpuState.BX = pageCount;
                machine.Spice86.Machine.CpuState.DX = 0xBEEF;
            }
        }, false, stateA => stateA.DX.Should().Be(0xBEEF));
    }

    /// <summary>INT 67h AH=59h (EMS hardware info): operation 0 and unsupported
    /// operations preserve AH, BX and DX.</summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void HardwareInfoUnwrittenOutputsArePreserved(byte operation) {
        RunEquivalence(new ServiceContractCase {
            Vector = 0x67,
            SelectorValue = 0x59,
            NeedsEms = true,
            Setup = machine => {
                State state = machine.Spice86.Machine.CpuState;
                state.AL = operation;
                state.BX = 0xBEEF;
                state.DX = 0xCAFE;
                state.ES = 0x0800;
                state.DI = 0x0100;
            }
        }, false, stateA => {
            stateA.AH.Should().Be(0x59);
            stateA.BX.Should().Be(0xBEEF);
            stateA.DX.Should().Be(0xCAFE);
        });
    }

    private void RunEquivalence(ServiceContractCase testCase, bool useLiteralOverload) {
        RunEquivalence(testCase, useLiteralOverload, _ => { });
    }

    private void RunEquivalence(ServiceContractCase testCase, bool useLiteralOverload,
        Action<State> assertExpected) {
        using ServiceContractMachine machineA = new(testCase.NeedsEms, testCase.NeedsXms);
        using ServiceContractMachine machineB = new(testCase.NeedsEms, testCase.NeedsXms);
        Spice86DependencyInjection spice86A = machineA.Spice86;
        Spice86DependencyInjection spice86B = machineB.Spice86;

        IInterruptHandler handler =
            spice86A.Machine.InstalledInterruptHandlers.Handlers[testCase.Vector];
        InterruptServiceEntry entry = ServiceContractHarness.EntryOf(handler,
            testCase.SelectorValue) ?? throw new InvalidOperationException(
                $"no service entry declared for {testCase}");
        ServiceContract contract = entry.Contract;
        ServiceSelector? selector = ServiceContractHarness.SelectorOf(handler);
        bool direct = entry.ExecutionPath == ServiceExecutionPath.Direct;

        // Same seeded inputs on both machines, like the contract harness does for its two runs.
        Random random = new(testCase.Vector * 131071 + testCase.SelectorValue * 131);
        ServiceContractHarness.RandomizeInputs(spice86A.Machine.CpuState, random);
        ServiceContractHarness.FillContractReads(spice86A.Machine.CpuState, selector,
            testCase.SelectorValue, contract);
        testCase.Setup?.Invoke(machineA);
        ServiceContractHarness.RandomizeInputs(spice86B.Machine.CpuState,
            new Random(testCase.Vector * 131071 + testCase.SelectorValue * 131));
        ServiceContractHarness.FillContractReads(spice86B.Machine.CpuState, selector,
            testCase.SelectorValue, contract);
        testCase.Setup?.Invoke(machineB);

        // Machine A: run `int N` at the scratch address in the interpreter. A Setup that
        // invokes services (like EMS handle allocation) leaves the selector register at
        // its own result value, so the caller sets it again right before the `int`.
        ServiceContractHarness.SetSelector(spice86A.Machine.CpuState, selector,
            testCase.SelectorValue);
        WriteInt(spice86A.Machine.Memory, testCase.Vector);
        State stateA = spice86A.Machine.CpuState;
        stateA.CS = ScratchSegment;
        stateA.IP = ScratchOffset;
        int steps = 0;
        while ((stateA.CS != ScratchSegment || stateA.IP != ExpectedReturnIp)
               && steps < StepLimit) {
            spice86A.Machine.CfgCpu.ExecuteNext();
            steps++;
        }
        (stateA.CS == ScratchSegment && stateA.IP == ExpectedReturnIp).Should().BeTrue(
            "the interpreter did not return from int 0x{0:X2} within {1} steps",
            testCase.Vector, StepLimit);
        assertExpected(stateA);

        // Machine B: an override at the vector's target runs the real stub in the
        // interpreter; the helper calls it through InterruptCall.
        WriteInt(spice86B.Machine.Memory, testCase.Vector);
        State stateB = spice86B.Machine.CpuState;
        SegmentedAddress target = spice86B.Machine.InterruptVectorTable[testCase.Vector];
        CSharpOverrideHelper helper = new(spice86B.FunctionCatalogue.FunctionInformations,
            spice86B.Machine, _logger, new Configuration { HttpApiPort = 0 });
        if (direct) {
            // A Direct service must never run its stub bytes: any dispatch to the
            // vector's target fails the case instead of silently falling back.
            helper.DefineFunction(target.Segment, target.Offset,
                _ => throw new UnrecoverableException(
                    $"Direct service {entry.Name} of int 0x{testCase.Vector:X2} ran its stub"),
                false, "stub_trap");
        } else {
            helper.DefineFunction(target.Segment, target.Offset, _ => {
                stateB.CS = target.Segment;
                stateB.IP = target.Offset;
                int stubSteps = 0;
                while ((stateB.CS != ScratchSegment || stateB.IP != ExpectedReturnIp)
                       && stubSteps < StepLimit) {
                    spice86B.Machine.CfgCpu.ExecuteNext();
                    stubSteps++;
                }
                if (stateB.CS != ScratchSegment || stateB.IP != ExpectedReturnIp) {
                    throw new UnrecoverableException(
                        $"Stub of int 0x{testCase.Vector:X2} did not return within {StepLimit} steps");
                }
                return () => { };
            }, false, "stub_runner");
        }

        // Save the parameter values the setup produced, then clobber what the helper must
        // write: a helper that forgets a parameter or the selector fails the comparison.
        IReadOnlyList<TypedParameter> parameters =
            ServiceCallSignature.Parameters(contract, selector);
        object[] args = new object[parameters.Count + 2];
        args[0] = ScratchSegment;
        args[1] = ExpectedReturnIp;
        for (int i = 0; i < parameters.Count; i++) {
            args[i + 2] = ReadParameter(stateB, parameters[i]);
        }
        // The literal is read at DS:DX before the parameter clobbering below loses them.
        byte? terminator = entry.StringArgument.Terminator();
        string? literal = null;
        if (useLiteralOverload
            && (terminator is null
                || !TryReadLiteral(machineB.Spice86, terminator.Value, out literal))) {
            return;
        }
        foreach (TypedParameter parameter in parameters) {
            ClobberParameter(stateB, parameter);
        }
        ClobberSelector(stateB, selector);
        foreach (CpuValues flag in new[] {
            CpuValues.Carry, CpuValues.Parity, CpuValues.Auxiliary,
            CpuValues.Zero, CpuValues.Sign, CpuValues.Overflow,
            CpuValues.Direction }.Where(flag => contract.Registers.Reads.HasFlag(flag))) {
            WriteFlag(stateB, flag, !ReadFlag(stateB, flag));
        }
        IReadOnlyList<TypedParameter> allRegisters =
            CpuValueParameters.Decompose(CpuValues.Registers);
        object?[] clobberedRegisters = direct
            ? allRegisters.Select(parameter => ReadParameter(stateB, parameter)).ToArray()
            : [];

        // The machine ran EXIT.COM to termination at startup, so IsRunning is false;
        // the override chain dispatcher of InterruptCall stops on a stopped machine.
        stateB.IsRunning = true;
        InterruptServiceGroups.TryGetPropertyName(handler.GetType(), out string? propertyName)
            .Should().BeTrue();
        PropertyInfo groupProperty = typeof(CSharpOverrideHelper).GetProperty(
            propertyName ?? throw new InvalidOperationException(
                $"no group property name for vector 0x{testCase.Vector:X2}"),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"CSharpOverrideHelper has no property {propertyName}");
        object group = groupProperty.GetValue(helper)
            ?? throw new InvalidOperationException($"property {propertyName} returned null");
        object[] invokeArgs = literal is null ? args : [.. args, literal];
        Type[] expectedArgumentTypes = invokeArgs.Select(a => a.GetType()).ToArray();
        MethodInfo method = group.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == entry.Name
                && m.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual(expectedArgumentTypes));
        object? returned;
        try {
            returned = method.Invoke(group, invokeArgs);
        } catch (ArgumentException e) {
            string expected = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            string actual = string.Join(", ", args.Select(a => a.GetType().Name));
            string parms = string.Join(", ", parameters.Select(p => $"{p.Name}/{p.Type}/{p.Mask}"));
            throw new InvalidOperationException(
                $"{entry.Name} expects ({expected}) but got ({actual}); parameters: {parms}", e);
        }

        // Assert the returned value carries exactly the contract outputs, in signature order.
        IReadOnlyList<TypedParameter> outputs = ServiceCallSignature.Outputs(contract);
        object?[] expectedOutputs = outputs.Select(output => ReadParameter(stateA, output)).ToArray();
        object?[] actualOutputs = returned switch {
            null => [],
            ITuple tuple => Enumerable.Range(0, tuple.Length).Select(i => tuple[i]).ToArray(),
            _ => [returned]
        };
        actualOutputs.Should().Equal(expectedOutputs,
            "the returned value of {0} must carry the contract outputs in order", entry.Name);

        if (direct) {
            // A Direct service stores no input, selector or output into State: the
            // caller's clobbered registers must be exactly what the call left.
            object?[] registersAfterCall = allRegisters
                .Select(parameter => ReadParameter(stateB, parameter)).ToArray();
            registersAfterCall.Should().Equal(clobberedRegisters,
                "the direct helper of {0} wrote a register outside its frame", entry.Name);
            // Emulate the generated caller's boundary: the original inputs, the selector
            // and the returned outputs land in registers exactly as after the stub run.
            for (int i = 0; i < parameters.Count; i++) {
                WriteParameter(stateB, parameters[i], args[i + 2]);
            }
            ServiceContractHarness.SetSelector(stateB, selector, testCase.SelectorValue);
            for (int i = 0; i < outputs.Count; i++) {
                WriteParameter(stateB, outputs[i], actualOutputs[i]);
            }
        }

        // Assert full machine state equality.
        stateB.EAX.Should().Be(stateA.EAX, "EAX after {0}", entry.Name);
        stateB.EBX.Should().Be(stateA.EBX, "EBX after {0}", entry.Name);
        stateB.ECX.Should().Be(stateA.ECX, "ECX after {0}", entry.Name);
        stateB.EDX.Should().Be(stateA.EDX, "EDX after {0}", entry.Name);
        stateB.ESI.Should().Be(stateA.ESI, "ESI after {0}", entry.Name);
        stateB.EDI.Should().Be(stateA.EDI, "EDI after {0}", entry.Name);
        stateB.EBP.Should().Be(stateA.EBP, "EBP after {0}", entry.Name);
        stateB.ESP.Should().Be(stateA.ESP, "ESP after {0}", entry.Name);
        stateB.DS.Should().Be(stateA.DS, "DS after {0}", entry.Name);
        stateB.ES.Should().Be(stateA.ES, "ES after {0}", entry.Name);
        stateB.FS.Should().Be(stateA.FS, "FS after {0}", entry.Name);
        stateB.GS.Should().Be(stateA.GS, "GS after {0}", entry.Name);
        stateB.SS.Should().Be(stateA.SS, "SS after {0}", entry.Name);
        stateB.CS.Should().Be(stateA.CS, "CS after {0}", entry.Name);
        stateB.IP.Should().Be(stateA.IP, "IP after {0}", entry.Name);
        stateB.Flags.FlagRegister.Should().Be(stateA.Flags.FlagRegister,
            "FLAGS after {0}", entry.Name);
        byte[] memoryA = ServiceContractHarness.SnapshotMemory(spice86A.Machine.Memory);
        byte[] memoryB = ServiceContractHarness.SnapshotMemory(spice86B.Machine.Memory);
        for (int i = 0; i < memoryA.Length; i++) {
            if (memoryA[i] != memoryB[i]) {
                memoryB[i].Should().Be(memoryA[i],
                    "memory differs at offset 0x{0:X} after {1}", i, entry.Name);
            }
        }
    }

    /// <summary>The string at <c>DS:DX</c> up to (not including) <paramref name="terminator"/>,
    /// when it would print as a literal: terminator within 64 bytes, no offset wrap, every byte
    /// before it printable ASCII, tab, LF or CR.</summary>
    private static bool TryReadLiteral(Spice86DependencyInjection spice86, byte terminator,
        out string literal) {
        literal = string.Empty;
        State state = spice86.Machine.CpuState;
        uint physical = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);
        System.Text.StringBuilder text = new();
        for (int i = 0; i <= 64; i++) {
            if (state.DX + i + 1 > 0x10000) {
                return false;
            }
            byte b = spice86.Machine.Memory.SneakilyRead(physical + (uint)i);
            if (b == terminator) {
                literal = text.ToString();
                return true;
            }
            if (b is not ((>= 0x20 and <= 0x7E) or 0x09 or 0x0A or 0x0D)) {
                return false;
            }
            text.Append((char)b);
        }
        return false;
    }

    private static void WriteInt(IMemory memory, byte vector) {
        memory.UInt8[ScratchLinear] = 0xCD;
        memory.UInt8[ScratchLinear + 1] = vector;
    }

    private static object ReadParameter(State state, TypedParameter parameter) =>
        StateProperty(parameter.Register).GetValue(state)
            ?? throw new UnrecoverableException($"unreadable {parameter.Register}");

    private static PropertyInfo StateProperty(string register) =>
        typeof(State).GetProperty(register)
            ?? throw new UnrecoverableException($"no State property {register}");

    private static void WriteParameter(State state, TypedParameter parameter, object? value) =>
        StateProperty(parameter.Register).SetValue(state, value);

    private static void ClobberParameter(State state, TypedParameter parameter) {
        PropertyInfo property = StateProperty(parameter.Register);
        object inverted = property.GetValue(state) switch {
            bool flag => !flag,
            byte value => (byte)~value,
            ushort value => (ushort)~value,
            uint value => ~value,
            _ => throw new UnrecoverableException($"cannot clobber {parameter.Register}")
        };
        property.SetValue(state, inverted);
    }

    private static void ClobberSelector(State state, ServiceSelector? selector) {
        switch (selector) {
            case ServiceSelector.Ah: state.AH = (byte)~state.AH; break;
            case ServiceSelector.Al: state.AL = (byte)~state.AL; break;
            case ServiceSelector.Ax: state.AX = (ushort)~state.AX; break;
        }
    }

    private static readonly IReadOnlyDictionary<CpuValues, string> FlagPropertyNames =
        new Dictionary<CpuValues, string> {
            [CpuValues.Carry] = nameof(State.CarryFlag),
            [CpuValues.Parity] = nameof(State.ParityFlag),
            [CpuValues.Auxiliary] = nameof(State.AuxiliaryFlag),
            [CpuValues.Zero] = nameof(State.ZeroFlag),
            [CpuValues.Sign] = nameof(State.SignFlag),
            [CpuValues.Overflow] = nameof(State.OverflowFlag),
            [CpuValues.Direction] = nameof(State.DirectionFlag),
        };

    private static bool ReadFlag(State state, CpuValues flag) =>
        (bool)(StateProperty(FlagPropertyNames[flag]).GetValue(state)
            ?? throw new UnrecoverableException($"unreadable {flag}"));

    private static void WriteFlag(State state, CpuValues flag, bool value) =>
        StateProperty(FlagPropertyNames[flag]).SetValue(state, value);
}
