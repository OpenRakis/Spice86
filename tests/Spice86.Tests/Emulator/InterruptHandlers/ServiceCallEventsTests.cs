namespace Spice86.Tests.Emulator.InterruptHandlers;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Checks that every DOS or BIOS service call raises one <see cref="IServiceCallObserver"/> event, and that a
/// call raises the same event from emulated code, from <c>InterruptCall</c> and from a direct call in a check
/// build.
/// </summary>
public sealed class ServiceCallEventsTests {
    private const byte DosVector = 0x21;

    private sealed class Recorder : IServiceCallObserver {
        public List<ServiceCall> Calls { get; } = [];

        public void OnServiceCall(in ServiceCall call) => Calls.Add(call);

        /// <summary>One line per call with everything but <see cref="ServiceCall.Direct"/>.</summary>
        public List<string> Lines => Calls.Select(Describe).ToList();

        private static string Describe(ServiceCall call) {
            string inputs = string.Join(",", call.Inputs.Select(input => $"{input.Binding}={input.Value:X}"));
            return $"{call.Vector:X2} {call.Selector:X} {call.Entry.Name} [{inputs}]";
        }
    }

    private static AsmRunSettings Observed(AsmRunSettings settings, Recorder recorder) {
        return settings with {
            CheckBuild = true,
            ConfigureMachine = machine => machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = recorder
        };
    }

    private static GeneratedCSharpProgram Generate(CfgPartitionedProgram program, GeneratorOptions options) {
        return new CfgCSharpGenerator(options with { CollectCensuses = true, Verify = true }).Generate(program);
    }

    [Fact]
    public void EmulatedRunRaisesOneEventPerServiceCall() {
        // Arrange
        AsmRunSettings settings = AsmFixtureCatalog.Partition("int_service_calls").Settings;
        Recorder recorder = new();

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(Observed(settings, recorder), JitMode.InterpretedOnly);

        // Assert: the program starts with AH=30h, AH=02h, AH=30h, AH=02h, AH=3Dh
        recorder.Calls.Should().OnlyContain(call => call.Vector == DosVector && !call.Direct);
        recorder.Calls.Take(5).Select(call => call.Selector).Should().Equal(0x30, 0x02, 0x30, 0x02, 0x3D);
        recorder.Calls[0].Entry.SelectorValue.Should().Be(0x30);
        recorder.Calls[0].Entry.Should().NotBeSameAs(InterruptServiceEntry.Unknown);
    }

    [Fact]
    public void RemovingTheObserverStopsTheEvents() {
        // Arrange
        AsmRunSettings settings = AsmFixtureCatalog.Partition("int_service_calls").Settings;
        Recorder recorder = new();
        AsmRunSettings unobserved = settings with {
            CheckBuild = true,
            ConfigureMachine = machine => {
                machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = recorder;
                machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = IServiceCallObserver.None;
            }
        };

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(unobserved, JitMode.InterpretedOnly);

        // Assert
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public void CheckBuildRaisesTheSameEventsAsTheEmulatedRun() {
        // Arrange: data_strings calls AH=40h and AH=3Dh directly and AH=09h through InterruptCall
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition("data_strings"));
        AsmRunSettings settings = discovery.Fixture.Settings;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCSharpProgram checkBuild = Generate(program, GeneratorOptions.CheckBuild);
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(checkBuild);
        Recorder emulated = new();
        Recorder generated = new();

        // Act
        using AsmFixtureRun emulatedRun =
            AsmFixtureRunner.RunSettings(Observed(settings, emulated), JitMode.InterpretedOnly);
        using AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunSettingsWithOverride(Observed(settings, generated), compiled.Supplier);

        // Assert: same services, same order, same inputs
        emulated.Calls.Should().NotBeEmpty();
        generated.Lines.Should().Equal(emulated.Lines);

        // Assert: the direct calls are marked, and carry the inputs the registers held
        emulated.Calls.Should().OnlyContain(call => !call.Direct);
        generated.Calls.Where(call => call.Direct).Select(call => call.Selector).Should().Equal(0x40, 0x3D);
        ServiceCall write = generated.Calls.Single(call => call.Selector == 0x40);
        write.Inputs.Select(input => input.Binding).Should()
            .Equal(CpuValues.Bx, CpuValues.Cx, CpuValues.Ds, CpuValues.Dx);
        write.Inputs[0].Value.Should().Be(1, "the program writes to the standard output handle");
        write.Inputs[1].Value.Should().Be(4, "the program writes four bytes");
        ServiceCall open = generated.Calls.Single(call => call.Selector == 0x3D);
        open.Inputs.Select(input => input.Binding).Should().Equal(CpuValues.Al, CpuValues.Ds | CpuValues.Dx);
        (open.Inputs[1].Value >> 16).Should().Be(write.Inputs[2].Value, "the name is addressed through DS");

        // Assert: with the branch probes of a check build too, each notification follows the caller stack save
        string notification = nameof(CSharpOverrideHelper.ServiceCalled) + "(";
        List<string> checkLines = checkBuild.SourceText.Split('\n').ToList();
        checkLines.Should().Contain(line => line.TrimStart().StartsWith(notification));
        for (int index = 0; index < checkLines.Count; index++) {
            if (checkLines[index].TrimStart().StartsWith(notification)) {
                checkLines[index - 1].Should().Contain(nameof(DosInt21Handler.RecordCallerStack) + "(");
            }
        }
    }

    [Fact]
    public void DefaultBuildPrintsNoNotificationAndRaisesNoEventForADirectCall() {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition("data_strings"));
        AsmRunSettings settings = discovery.Fixture.Settings;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCSharpProgram defaultBuild = Generate(program, GeneratorOptions.Default);
        GeneratedCSharpProgram checkBuild =
            Generate(program, GeneratorOptions.Default with { ServiceCallNotifications = true });
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(defaultBuild);
        Recorder generated = new();

        // Act
        using AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunSettingsWithOverride(Observed(settings, generated), compiled.Supplier);

        // Assert: the check build is the default build plus, per direct call, the duplicate caller stack
        // save and the ServiceCalled line that follows it
        string notification = nameof(CSharpOverrideHelper.ServiceCalled) + "(";
        string callerStackSave = nameof(DosInt21Handler.RecordCallerStack) + "(SS, SP);";
        defaultBuild.SourceText.Should().NotContain(notification);
        List<string> checkLines = checkBuild.SourceText.Split('\n').ToList();
        List<string> notifications = checkLines.Where(line => line.TrimStart().StartsWith(notification)).ToList();
        notifications.Should().HaveCount(2);
        notifications.Should().Contain(line => line.TrimStart().StartsWith(notification + "0x21, 0x40, "));
        notifications.Should().Contain(line => line.TrimStart().StartsWith(notification + "0x21, 0x3D, "));
        checkLines.Where((line, index) => !line.TrimStart().StartsWith(notification)
                && !(line.Contains(callerStackSave)
                    && checkLines[index + 1].TrimStart().StartsWith(notification))).Should()
            .Equal(defaultBuild.SourceText.Split('\n'));

        // Assert: each notification follows the caller stack save, as the event follows it through the stub
        for (int index = 0; index < checkLines.Count; index++) {
            if (checkLines[index].TrimStart().StartsWith(notification)) {
                checkLines[index - 1].Should().Contain(nameof(DosInt21Handler.RecordCallerStack) + "(");
            }
        }

        // Assert: only the calls that go through the stub are reported
        generated.Calls.Should().NotBeEmpty();
        generated.Calls.Should().OnlyContain(call => !call.Direct);
        generated.Calls.Select(call => call.Selector).Should().NotContain(new int?[] { 0x40, 0x3D });
    }

    [Fact]
    public void ServiceCalledRaisesADirectEventWithTheEntryOfTheSelector() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2", installInterruptVectors: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });
        Recorder recorder = new();
        machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = recorder;
        machine.InstalledInterruptHandlers.TryGetHandler(DosVector, out IInterruptHandler? dos).Should().BeTrue();
        InterruptServiceEntry write = ((HandlerServices.SelectedServices)dos!.Services).Services[0x40];

        // Act
        helper.ServiceCalled(DosVector, 0x40, 1, 4, 0x2000, 0x0100);

        // Assert
        recorder.Calls.Should().ContainSingle();
        ServiceCall call = recorder.Calls[0];
        call.Vector.Should().Be(DosVector);
        call.Selector.Should().Be(0x40);
        call.Direct.Should().BeTrue();
        call.Entry.Should().BeSameAs(write);
        call.Inputs.Should().Equal(
            new ServiceCallInput(write.Inputs[0], 1), new ServiceCallInput(write.Inputs[1], 4),
            new ServiceCallInput(write.Inputs[2], 0x2000), new ServiceCallInput(write.Inputs[3], 0x0100));
    }

    [Fact]
    public void ServiceCalledRejectsAWrongNumberOfInputs() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2", installInterruptVectors: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });
        machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = new Recorder();

        // Act
        Action act = () => helper.ServiceCalled(DosVector, 0x40, 1);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ServiceCalledDoesNothingWithoutAnObserverOrForAHandlerThatRaisesNoEvents() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2", installInterruptVectors: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });
        Recorder recorder = new();

        // Act: no observer, so the wrong input count is never looked at
        helper.ServiceCalled(DosVector, 0x40, 1);

        // Act: the INT 1Ch stub is a bare iret and raises no event through the stub either
        machine.InstalledInterruptHandlers.ServiceCallEvents.Observer = recorder;
        helper.ServiceCalled(0x1C, null);

        // Assert
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public void HandlersOnHardwareInterruptVectorsRaiseNoEvent() {
        // Arrange
        const byte keyboardIrqVector = 0x09;
        using Spice86Creator creator = new(binName: "jump2", installInterruptVectors: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        InstalledInterruptHandlers installed = machine.InstalledInterruptHandlers;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });
        Recorder recorder = new();

        // Act
        installed.ServiceCallEvents.Observer = recorder;
        helper.ServiceCalled(keyboardIrqVector, null);

        // Assert: the INT 09h handler derives from InterruptHandler, but IRQ 1 delivers it
        installed.HardwareVectorNumbers.Should().Contain(keyboardIrqVector);
        installed.ServiceCallEvents.ObserverFor(keyboardIrqVector).Should().BeNull();
        installed.ServiceCallEvents.ObserverFor(DosVector).Should().BeSameAs(recorder);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public void EveryInputBindingOfAnInstalledHandlerCanBeRead() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2", installInterruptVectors: true, enableXms: true,
            enableEms: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        List<InterruptServiceEntry> entries = [];
        foreach (IInterruptHandler handler in machine.InstalledInterruptHandlers.Handlers.Values) {
            switch (handler.Services) {
                case HandlerServices.SingleService single:
                    entries.Add(single.Service);
                    break;
                case HandlerServices.SelectedServices selected:
                    entries.AddRange(selected.Services.Values);
                    break;
            }
        }

        // Act
        Action act = () => {
            foreach (InterruptServiceEntry entry in entries) {
                ServiceCallInputs.ReadAll(machine.CpuState, entry);
            }
        };

        // Assert
        entries.Should().Contain(entry => entry.Implementation != null && entry.Inputs.Count > 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void InputsAreEncodedByBindingWidth() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        State state = dependencyInjection.Machine.CpuState;
        state.AX = 0x1234;
        state.DS = 0xABCD;
        state.DX = 0x00EF;
        state.CarryFlag = true;
        state.ZeroFlag = false;

        // Act and Assert
        ServiceCallInputs.Read(state, CpuValues.Al).Should().Be(0x34);
        ServiceCallInputs.Read(state, CpuValues.Ah).Should().Be(0x12);
        ServiceCallInputs.Read(state, CpuValues.Ax).Should().Be(0x1234);
        ServiceCallInputs.Read(state, CpuValues.Ds).Should().Be(0xABCD);
        ServiceCallInputs.Read(state, CpuValues.Carry).Should().Be(1);
        ServiceCallInputs.Read(state, CpuValues.Zero).Should().Be(0);
        ServiceCallInputs.Read(state, CpuValues.Ds | CpuValues.Dx).Should().Be(0xABCD00EF);
        ServiceCallInputs.EncodingOf(CpuValues.Ds | CpuValues.Dx).Should()
            .Be(ServiceCallInputs.Encoding.SegmentedAddress);
    }

    [Fact]
    public void EveryRegisterAndFlagBindingReadsTheRegisterOrFlagItNames() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        State state = dependencyInjection.Machine.CpuState;
        state.EAX = 0x11223344;
        state.ECX = 0x55667788;
        state.EDX = 0x99AABBCC;
        state.EBX = 0xDDEEF001;
        state.ESP = 0x02030405;
        state.EBP = 0x06070809;
        state.ESI = 0x0A0B0C0D;
        state.EDI = 0x0E0F1011;
        state.ES = 0x1213;
        state.CS = 0x1415;
        state.SS = 0x1617;
        state.DS = 0x1819;
        state.FS = 0x1A1B;
        state.GS = 0x1C1D;
        state.CarryFlag = true;
        state.ParityFlag = false;
        state.AuxiliaryFlag = true;
        state.ZeroFlag = false;
        state.SignFlag = true;
        state.OverflowFlag = false;
        state.DirectionFlag = true;
        (CpuValues Binding, uint Expected)[] cases = [
            (CpuValues.Al, state.AL), (CpuValues.Ah, state.AH), (CpuValues.Ax, state.AX), (CpuValues.Eax, state.EAX),
            (CpuValues.Cl, state.CL), (CpuValues.Ch, state.CH), (CpuValues.Cx, state.CX), (CpuValues.Ecx, state.ECX),
            (CpuValues.Dl, state.DL), (CpuValues.Dh, state.DH), (CpuValues.Dx, state.DX), (CpuValues.Edx, state.EDX),
            (CpuValues.Bl, state.BL), (CpuValues.Bh, state.BH), (CpuValues.Bx, state.BX), (CpuValues.Ebx, state.EBX),
            (CpuValues.Sp, state.SP), (CpuValues.Esp, state.ESP), (CpuValues.Bp, state.BP), (CpuValues.Ebp, state.EBP),
            (CpuValues.Si, state.SI), (CpuValues.Esi, state.ESI), (CpuValues.Di, state.DI), (CpuValues.Edi, state.EDI),
            (CpuValues.Es, state.ES), (CpuValues.Cs, state.CS), (CpuValues.Ss, state.SS),
            (CpuValues.Ds, state.DS), (CpuValues.Fs, state.FS), (CpuValues.Gs, state.GS),
            (CpuValues.Carry, 1), (CpuValues.Parity, 0), (CpuValues.Auxiliary, 1), (CpuValues.Zero, 0),
            (CpuValues.Sign, 1), (CpuValues.Overflow, 0), (CpuValues.Direction, 1)];

        // Act
        (CpuValues Binding, uint Value)[] read = cases
            .Select(testCase => (testCase.Binding, ServiceCallInputs.Read(state, testCase.Binding))).ToArray();

        // Assert
        read.Should().Equal(cases.Select(testCase => (testCase.Binding, testCase.Expected)));
    }

    [Theory]
    [InlineData(CpuValues.None)]
    [InlineData(CpuValues.EaxHigh)]
    [InlineData(CpuValues.Al | CpuValues.Ch)]
    [InlineData(CpuValues.Ax | CpuValues.Es)]
    [InlineData(CpuValues.Es | CpuValues.Ds)]
    [InlineData(CpuValues.Carry | CpuValues.Zero)]
    [InlineData(CpuValues.FpuControlWord)]
    public void ABindingThatIsNotASingleRegisterFlagOrDsDxIsRejected(CpuValues binding) {
        // Act
        Action act = () => ServiceCallInputs.EncodingOf(binding);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
