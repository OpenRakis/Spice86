namespace Spice86.Tests;

using FluentAssertions;
using FluentAssertions.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

public class GeneratedCodeMetricsCollectorTest {
    [Fact]
    public void CountsInvocationsOnEveryAluWidth() {
        // Arrange
        string body = """
            Alu8.Sub(AL, 1);
            AX = Alu16.Add(AX, 2);
            Alu32.UpdateFlags(EAX);
            Stack.Push16(AX);
            ((dynamic)Alu16).Add(AX, 3);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.AluCalls.Should().Be(3);
    }

    [Fact]
    public void CountsStatusFlagReadsButNotAssignments() {
        // Arrange
        string body = """
            if (ZeroFlag) { }
            CarryFlag = true;
            CarryFlag = !CarryFlag;
            bool less = SignFlag != OverflowFlag;
            bool parity = State.ParityFlag;
            State.AuxiliaryFlag = AuxiliaryFlag;
            bool notCounted = InterruptFlag && DirectionFlag;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.StatusFlagReads.Should().Be(6);
    }

    [Fact]
    public void CountsStackPushAndPopCalls() {
        // Arrange
        string body = """
            Stack.Push16(AX);
            Stack.Pop16();
            Stack.Push32(EAX);
            Stack.Pop32();
            Stack.Peek16(0);
            Alu8.Sub(AL, 1);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.StackCalls.Should().Be(4);
    }

    [Fact]
    public void CountsStatusFlagAssignmentsSeparatelyFromReads() {
        // Arrange
        string body = """
            CarryFlag = true;
            ZeroFlag = AX == 0;
            CarryFlag &= ZeroFlag;
            InterruptFlag = false;
            bool read = ParityFlag;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.FlagAssignments.Should().Be(3);
        metrics.StatusFlagReads.Should().Be(2);
    }

    [Fact]
    public void CountsOnlyLocalDeclarationsDirectlyInTheMethodBody() {
        // Arrange
        string body = """
            ushort flagOperand_0001 = UInt8[DS, 0x0401];
            AX += flagOperand_0001;
            if (ZeroFlag) { ushort nested = 1; AX += nested; }
            while (true) { ushort alsoNested = 2; AX += alsoNested; }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.MethodVariables.Should().Be(1);
    }

    [Fact]
    public void CountsCompoundAndTupleAssignments() {
        // Arrange
        string body = """
            DX += 1;
            CX -= 2;
            AX &= 0xFF;
            BX |= CX;
            DI ^= 3;
            SI <<= 4;
            DX >>= 1;
            AX /= 2;
            (BX, AX) = (AX, BX);
            AX = BX;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.CompoundAssignments.Should().Be(7);
        metrics.TupleSwaps.Should().Be(1);
    }

    [Fact]
    public void CountsStructuredStatements() {
        // Arrange
        string body = """
            do { break; } while (CX != 0);
            while (CX != 0) { }
            if (ZeroFlag) { } else if (CarryFlag) { }
            switch (AX) { case 1: break; }
            while (true) { continue; }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.DoLoops.Should().Be(1);
        metrics.WhileTrueLoops.Should().Be(1);
        metrics.IfStatements.Should().Be(2);
        metrics.BreakStatements.Should().Be(2);
        metrics.ContinueStatements.Should().Be(1);
    }

    [Fact]
    public void CountsRegisterAndLocalTrafficBySymbol() {
        // Arrange
        string body = """
            ushort value = AX;
            BX = value;
            CX += AX;
            DX++;
            (SI, DI) = (BX, CX);
            State.AX = State.DX;
            if (value != 0) {
                ushort nested = value;
                BX = nested;
            }
            """;

        // Act
        GeneratedCompilation compilation = CompileMethodBody(body);
        using MemoryStream stream = compilation.PeStream;
        GeneratedCodeMetrics metrics = GeneratedCodeMetricsCollector.Collect("test", compilation);

        // Assert
        using (new AssertionScope()) {
            metrics.RegisterReads.Should().Be(7);
            metrics.RegisterWrites.Should().Be(7);
            metrics.PlainRegisterAssignments.Should().Be(3);
            metrics.LocalVariables.Should().Be(2);
            metrics.LocalReads.Should().Be(4);
            metrics.LocalWrites.Should().Be(2);
            metrics.WriteBacks.Should().Be(2);
            metrics.Statements.Should().Be(9);
        }
    }

    [Theory]
    [InlineData("value", "nested")]
    [InlineData("unrelated", "other")]
    [InlineData("ax1", "bx99")]
    public void RenamingLocalsDoesNotChangeTrafficMetrics(string first, string second) {
        // Arrange
        string body = $$"""
            ushort {{first}} = AX;
            BX = {{first}};
            if ({{first}} != 0) {
                ushort {{second}} = {{first}};
                CX = (ushort){{second}};
            }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        using (new AssertionScope()) {
            metrics.RegisterReads.Should().Be(1);
            metrics.RegisterWrites.Should().Be(2);
            metrics.PlainRegisterAssignments.Should().Be(2);
            metrics.LocalVariables.Should().Be(2);
            metrics.LocalReads.Should().Be(4);
            metrics.LocalWrites.Should().Be(2);
            metrics.WriteBacks.Should().Be(2);
            metrics.Statements.Should().Be(5);
        }
    }

    [Fact]
    public void LocalNamedAxIsNotACpuRegister() {
        // Arrange
        string body = """
            ushort AX = State.AX;
            AX++;
            State.AX = AX;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.RegisterReads.Should().Be(1);
        metrics.RegisterWrites.Should().Be(1);
        metrics.PlainRegisterAssignments.Should().Be(1);
        metrics.LocalVariables.Should().Be(1);
        metrics.LocalReads.Should().Be(2);
        metrics.LocalWrites.Should().Be(2);
        metrics.WriteBacks.Should().Be(1);
    }

    [Fact]
    public void CountsEachDeclaredLocalAndEachTupleDestination() {
        // Arrange
        string body = """
            ushort a = 1, b;
            b = a;
            { ushort nested = b; AX = nested; }
            (ushort c, ushort d) = (a, b);
            (ushort e, _) = (c, d);
            for (int i = 0; i < 1; i++) { AX = (ushort)i; }
            ushort.TryParse("1", out ushort parsed);
            BX = parsed;
            CX = e;
            (SI, DI) = (BX, CX);
            (BX, CX) = (CX, BX);
            (AX, DX, CarryFlag) = Dos.SeekFile(SeekOrigin.Begin, 1, 0, 7);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        using (new AssertionScope()) {
            metrics.LocalVariables.Should().Be(8);
            metrics.LocalReads.Should().Be(12);
            metrics.LocalWrites.Should().Be(9);
            metrics.RegisterReads.Should().Be(4);
            metrics.RegisterWrites.Should().Be(10);
            metrics.PlainRegisterAssignments.Should().Be(4);
            metrics.TupleSwaps.Should().Be(1);
            metrics.WriteBacks.Should().Be(4);
        }
    }

    [Fact]
    public void CountsReferenceArgumentsAndSkipsNameof() {
        // Arrange
        string body = """
            ushort value = AX;
            Change(ref value, out ushort output, in value);
            BX = output;
            UInt16[DS, SI] = value;
            string name = nameof(State.AX);
            string other = nameof(value);
            Console.WriteLine(name + other);
            static void Change(ref ushort input, out ushort result, in ushort source) {
                result = source;
                input++;
            }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.RegisterReads.Should().Be(3);
        metrics.RegisterWrites.Should().Be(1);
        metrics.LocalVariables.Should().Be(4);
        metrics.LocalReads.Should().Be(6);
        metrics.LocalWrites.Should().Be(5);
        metrics.WriteBacks.Should().Be(1);
    }

    [Fact]
    public void CountsPatternCatchAndForeachLocals() {
        // Arrange
        string body = """
            object candidate = AX;
            if (candidate is ushort number) { BX = number; }
            foreach (ushort item in new ushort[] { 1, 2 }) { CX = item; }
            try { Console.WriteLine(candidate); }
            catch (Exception error) { Console.WriteLine(error.Message); }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.LocalVariables.Should().Be(4);
        metrics.LocalWrites.Should().Be(4);
        metrics.LocalReads.Should().Be(5);
        metrics.RegisterReads.Should().Be(1);
        metrics.RegisterWrites.Should().Be(2);
    }

    [Fact]
    public void DistinguishesOperationsFromBookkeepingAndPortCalls() {
        // Arrange
        string body = """
            (AX, DX, CarryFlag) = this.Dos.SeekFile(SeekOrigin.Begin, 1, 0, 7);
            this.Dos.RecordCallerStack(SS, SP);
            this.Vga.SetPaletteEntry(1, 2, 3, 4);
            Pit.ProgramChannel(0, 0x34, 1);
            Opl.WriteRegister(0x388, 1, 2);
            VgaPorts.WriteDacData(4);
            Out8(0x3C9, 4);
            Machine.IoPortDispatcher.WriteByte(0x3C9, 4);
            // Dos.SeekFile; Vga.SetPaletteEntry; Out8
            Console.WriteLine("Dos.SeekFile; Vga.SetPaletteEntry; Out8");
            OtherOperations Dos = new OtherOperations();
            OtherOperations Vga = Dos;
            Dos.SeekFile();
            Vga.SetPaletteEntry();
            Dos.WriteDacData();
            Dos.Out8();
            Dos.AX++;
            """;
        string members = """
            private sealed class OtherOperations {
                public ushort AX { get; set; }
                public void SeekFile() { }
                public void SetPaletteEntry() { }
                public void WriteDacData() { }
                public void Out8() { }
            }
            """;

        // Act
        GeneratedCompilation compilation = CompileMethodBody(body, members);
        using MemoryStream stream = compilation.PeStream;
        GeneratedCodeMetrics metrics = GeneratedCodeMetricsCollector.Collect("operations", compilation);

        // Assert
        using (new AssertionScope()) {
            metrics.ServiceCallsByReceiver.Should().BeEquivalentTo(new Dictionary<string, int> { ["Dos"] = 1 });
            metrics.ServiceCallsByOperation.Should().BeEquivalentTo(new Dictionary<string, int> { ["Dos.SeekFile"] = 1 });
            metrics.DeviceCallsByReceiver.Should().BeEquivalentTo(new Dictionary<string, int> {
                ["Vga"] = 1, ["Pit"] = 1, ["Opl"] = 1
            });
            metrics.DeviceCallsByOperation.Should().BeEquivalentTo(new Dictionary<string, int> {
                ["Vga.SetPaletteEntry"] = 1, ["Pit.ProgramChannel"] = 1, ["Opl.WriteRegister"] = 1
            });
            metrics.BookkeepingCalls.Should().Be(1);
            metrics.PortWrapperCalls.Should().Be(1);
            metrics.RawIoCalls.Should().Be(2);
            metrics.InterruptCalls.Should().Be(0);
            metrics.RegisterReads.Should().Be(2);
            metrics.RegisterWrites.Should().Be(2);
        }
    }

    [Fact]
    public void CountsImplementationsDeclaredOnPrivateInterruptWrappers() {
        // Arrange
        string body = """
            AX = Equipment.GetEquipmentList();
            AX = MemorySize.GetConventionalMemorySize();
            (BX, ES) = Dos.ReadListOfListsAddress();
            (AL, SI, DS, CarryFlag) = Dos.ReadLeadByteTable(0, SI, DS, CarryFlag);
            AL = Dos.FcbOpenFile(DS, DX);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.ServiceCallsByReceiver.Should().BeEquivalentTo(new Dictionary<string, int> {
            ["Equipment"] = 1, ["MemorySize"] = 1, ["Dos"] = 3
        });
        metrics.ServiceCallsByOperation.Should().BeEquivalentTo(new Dictionary<string, int> {
            ["Equipment.GetEquipmentList"] = 1, ["MemorySize.GetConventionalMemorySize"] = 1,
            ["Dos.ReadListOfListsAddress"] = 1, ["Dos.ReadLeadByteTable"] = 1, ["Dos.FcbOpenFile"] = 1
        });
    }

    [Fact]
    public void ParametersAndFieldsAreNotLocalWriteBacks() {
        // Arrange
        GeneratedCompilation compilation = CompileMethodBody("AX = (ushort)(BX + CX);", """
            private readonly ushort cs1 = 0x1234;
            public void Entry(ushort ax) { AX = ax; CS = cs1; }
            """);
        using MemoryStream stream = compilation.PeStream;

        // Act
        GeneratedCodeMetrics metrics = GeneratedCodeMetricsCollector.Collect("writebacks", compilation);

        // Assert
        metrics.WriteBacks.Should().Be(0);
        metrics.RegisterWrites.Should().Be(3);
        metrics.PlainRegisterAssignments.Should().Be(3);
        metrics.LocalVariables.Should().Be(0);
        metrics.LocalReads.Should().Be(0);
        CollectFromMethodBody("ushort ax = AX; AX += ax;").WriteBacks.Should().Be(0);
    }

    [Theory]
    [InlineData("ushort", "byte", "AL", "AH", "BL", "CL", 8, "packed")]
    [InlineData("uint", "ushort", "AX", "DX", "BX", "CX", 16, "value1")]
    [InlineData("ulong", "uint", "EAX", "EDX", "EBX", "ECX", 32, "unrelated")]
    public void PackedLocalHalvesRemainWriteBacks(string wideType, string narrowType,
        string low, string high, string shifted, string computed, int width, string name) {
        // Arrange
        string body = $$"""
            {{wideType}} {{name}} = 0x1234;
            {{low}} = ({{narrowType}}){{name}};
            {{high}} = ({{narrowType}})({{name}} >> {{width}});
            {{shifted}} = ({{narrowType}})({{name}} >> 1);
            {{computed}} = ({{narrowType}})({{name}} + 1);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        using (new AssertionScope()) {
            metrics.WriteBacks.Should().Be(2);
            metrics.RegisterWrites.Should().Be(4);
            metrics.PlainRegisterAssignments.Should().Be(4);
            metrics.LocalVariables.Should().Be(1);
            metrics.LocalReads.Should().Be(4);
            metrics.LocalWrites.Should().Be(1);
            metrics.Statements.Should().Be(5);
        }
    }

    [Fact]
    public void NarrowingBeforeAShiftIsNotAnUpperHalfWriteBack() {
        // Arrange
        const string body = """
            uint value = 0x12345678;
            AX = (ushort)((ushort)value >> 16);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.WriteBacks.Should().Be(0);
        metrics.RegisterWrites.Should().Be(1);
        metrics.LocalReads.Should().Be(1);
    }

    [Fact]
    public void GenericInterruptReplacementFailsReadabilityGates() {
        // Arrange
        GeneratedCodeMetrics before = CollectFromMethodBody(
            "(AX, DX, CarryFlag) = Dos.SeekFile(SeekOrigin.Begin, 1, 0, 7);");
        GeneratedCodeMetrics after = CollectFromMethodBody(
            "AH = 0x42; AL = 0; BX = 1; CX = 0; DX = 7; InterruptCall(0, 0, 0x21);");
        Dictionary<string, int> minimum = new() { ["serviceCalls.Dos"] = 1 };
        Dictionary<string, int> maximum = new() { ["InterruptCalls"] = 0 };

        // Act
        IReadOnlyList<string> failures = GeneratedCodeMetricsAssertions.FindReadabilityRegressions(after, minimum, maximum);

        // Assert
        GeneratedCodeMetricsAssertions.FindReadabilityRegressions(before, minimum, maximum).Should().BeEmpty();
        failures.Should().HaveCount(2);
        failures.Should().Contain("test: serviceCalls.Dos minimum=1, actual=0, delta=-1.");
        failures.Should().Contain("test: InterruptCalls maximum=0, actual=1, delta=+1.");
    }

    [Fact]
    public void DeviceSequenceExpansionFailsReadabilityGates() {
        // Arrange
        GeneratedCodeMetrics before = CollectFromMethodBody("Vga.SetPaletteEntry(1, 2, 3, 4);");
        GeneratedCodeMetrics after = CollectFromMethodBody("""
            VgaPorts.WriteDacWriteIndex(1);
            VgaPorts.WriteDacData(2);
            VgaPorts.WriteDacData(3);
            VgaPorts.WriteDacData(4);
            """);
        Dictionary<string, int> minimum = new() { ["deviceCalls.Vga"] = 1 };
        Dictionary<string, int> maximum = new() { ["PortWrapperCalls"] = 0 };

        // Act
        IReadOnlyList<string> failures = GeneratedCodeMetricsAssertions.FindReadabilityRegressions(after, minimum, maximum);

        // Assert
        GeneratedCodeMetricsAssertions.FindReadabilityRegressions(before, minimum, maximum).Should().BeEmpty();
        failures.Should().HaveCount(2);
        failures.Should().Contain("test: deviceCalls.Vga minimum=1, actual=0, delta=-1.");
        failures.Should().Contain("test: PortWrapperCalls maximum=0, actual=4, delta=+4.");
    }

    [Fact]
    public void FixtureResultsCannotCompensateForEachOther() {
        // Arrange
        GeneratedCodeMetrics loss = CollectFromMethodBody("InterruptCall(0, 0, 0x21);") with { Fixture = "loss" };
        GeneratedCodeMetrics gain = CollectFromMethodBody("""
            (AX, DX, CarryFlag) = Dos.SeekFile(SeekOrigin.Begin, 1, 0, 7);
            (AX, DX, CarryFlag) = Dos.SeekFile(SeekOrigin.Begin, 1, 0, 7);
            """) with { Fixture = "gain" };
        Dictionary<string, int> minimum = new() { ["serviceCalls.Dos"] = 1 };
        Dictionary<string, int> maximum = new() { ["InterruptCalls"] = 0 };

        // Act
        IReadOnlyList<string> failures = GeneratedCodeMetricsAssertions.FindReadabilityRegressions(loss, minimum, maximum);
        IReadOnlyList<string> improvements = GeneratedCodeMetricsAssertions.FindReadabilityRegressions(gain, minimum, maximum);

        // Assert
        improvements.Should().BeEmpty();
        failures.Should().HaveCount(2).And.OnlyContain(message => message.StartsWith("loss:", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingBaselineEntryIsAnError() {
        // Arrange
        IReadOnlyDictionary<string, GeneratedCodeMetricsAssertions.ReadabilityBaseline> entries =
            GeneratedCodeMetricsAssertions.ParseBaseline(ValidBaselineMetadataJson(),
                new Dictionary<string, string> { ["test"] = ValidBaselineJson() });

        // Act
        Action read = () => GeneratedCodeMetricsAssertions.GetBaseline("missing", entries);

        // Assert
        read.Should().Throw<InvalidDataException>().WithMessage("*missing*");
    }

    [Theory]
    [InlineData("missingMetric")]
    [InlineData("unknownMetric")]
    [InlineData("negative")]
    [InlineData("schema")]
    [InlineData("hash")]
    [InlineData("missingField")]
    [InlineData("direction")]
    [InlineData("referenceRevision")]
    [InlineData("missingMetadataField")]
    public void RejectsInvalidBaseline(string defect) {
        // Arrange
        JsonObject metadata = JsonNode.Parse(ValidBaselineMetadataJson())?.AsObject() ?? throw new InvalidOperationException();
        JsonObject fixture = JsonNode.Parse(ValidBaselineJson())?.AsObject() ?? throw new InvalidOperationException();
        JsonObject maximum = fixture["maximum"]?.AsObject() ?? throw new InvalidOperationException();
        switch (defect) {
            case "missingMetric": maximum.Remove("RegisterReads"); break;
            case "unknownMetric": maximum["MadeUpCount"] = 0; break;
            case "negative": maximum["RegisterReads"] = -1; break;
            case "schema": metadata["schemaVersion"] = 1; break;
            case "referenceRevision": metadata["referenceRevision"] = "invalid"; break;
            case "missingMetadataField": metadata.Remove("referenceRevision"); break;
            case "hash": fixture["sourceSha256"] = "invalid"; break;
            case "missingField": fixture.Remove("minimum"); break;
            case "direction": maximum["serviceCalls.Dos"] = 0; break;
        }

        // Act
        Action read = () => GeneratedCodeMetricsAssertions.ParseBaseline(metadata.ToJsonString(),
            new Dictionary<string, string> { ["test"] = fixture.ToJsonString() });

        // Assert
        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BindingErrorsCannotBecomeZeroCounts() {
        // Arrange
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(["class Broken { void M() { AX = missing; } }"]);
        using MemoryStream stream = compilation.PeStream;

        // Act
        Action collect = () => GeneratedCodeMetricsCollector.Collect("broken", compilation);

        // Assert
        collect.Should().Throw<InvalidOperationException>().WithMessage("*Cannot measure unbound*");
    }

    [Fact]
    public void MethodRowsIncludeConstructorsAndNestedBodiesWithoutDuplicatingInitializers() {
        // Arrange
        GeneratedCompilation compilation = CompileMethodBody("""
            Action action = () => { AX = BX; };
            void Nested() { CX = DX; }
            action();
            Nested();
            """, """
            private static Spice86.Core.Emulator.CPU.State Cpu => null;
            private static readonly ushort Initial = Cpu.SI;
            public ushort Value => Cpu.DI;
            public void Method(int value) { AX = (ushort)value; }
            """);
        using MemoryStream stream = compilation.PeStream;

        // Act
        GeneratedCodeMetrics total = GeneratedCodeMetricsCollector.Collect("methods", compilation);
        IReadOnlyDictionary<string, GeneratedCodeMetrics> methods = GeneratedCodeMetricsCollector.CollectMethods("methods", compilation);

        // Assert
        methods.Keys.Should().BeEquivalentTo(["Generated.Generated()", "Generated.Method()", "Generated.Method(int)"]);
        methods["Generated.Method()"].RegisterReads.Should().Be(2);
        methods["Generated.Method()"].RegisterWrites.Should().Be(2);
        methods["Generated.Method()"].LocalVariables.Should().Be(1);
        methods["Generated.Method(int)"].RegisterWrites.Should().Be(1);
        total.RegisterReads.Should().Be(4);
        methods.Values.Sum(row => row.RegisterReads).Should().Be(2, "field initializers and accessors remain in fixture totals");
        methods.Values.Should().OnlyContain(row => row.SourceLocation.StartsWith("tree=0,line=", StringComparison.Ordinal));
        methods.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void NormalAssertionsNeverWriteTheBaseline() {
        // Arrange
        Dictionary<string, byte[]> before = Directory.EnumerateFiles(GeneratedCodeMetricsAssertions.BaselineDirectory)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        GeneratedCodeMetrics metrics = CollectFromMethodBody("AX = BX;");

        // Act
        Action assert = () => GeneratedCodeMetricsAssertions.AssertInvariants(metrics, "AX = BX;",
            new Dictionary<string, GeneratedCodeMetrics>());

        // Assert
        assert.Should().Throw<InvalidDataException>().WithMessage("*test*");
        Directory.EnumerateFiles(GeneratedCodeMetricsAssertions.BaselineDirectory).Should().BeEquivalentTo(before.Keys);
        foreach ((string path, byte[] bytes) in before) {
            File.ReadAllBytes(path).Should().Equal(bytes);
        }
    }

    private static string ValidBaselineMetadataJson() => JsonSerializer.Serialize(new {
        schemaVersion = 2,
        referenceRevision = new string('a', 40)
    });

    private static string ValidBaselineJson() => JsonSerializer.Serialize(new {
        sourceSha256 = new string('b', 64),
        minimum = GeneratedCodeMetricsAssertions.MandatoryMinimumMetrics.ToDictionary(key => key, _ => 0),
        maximum = GeneratedCodeMetricsAssertions.MandatoryMaximumMetrics.ToDictionary(key => key, _ => 0)
    });

    private static GeneratedCompilation CompileMethodBody(string body) => CompileMethodBody(body, "");

    private static GeneratedCompilation CompileMethodBody(string body, string members) {
        string source = $$"""
            using System;
            using System.IO;
            using Spice86.Core.Emulator.ReverseEngineer;
            abstract class Generated : CSharpOverrideHelper {
                protected Generated() : base(null, null, null, null) { }
                public void Method() {
                    {{body}}
                }
                {{members}}
            }
            """;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([source]);
        // Minimal counter probes can leave unused values. Real generated programs keep warnings as errors.
        CSharpCompilation binding = compilation.Compilation.WithOptions(
            compilation.Compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Default));
        compilation = new GeneratedCompilation([source], binding, compilation.EmitResult, compilation.PeStream);
        binding.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return compilation;
    }

    private static GeneratedCodeMetrics CollectFromMethodBody(string body) {
        GeneratedCompilation compilation = CompileMethodBody(body);
        using MemoryStream stream = compilation.PeStream;
        return GeneratedCodeMetricsCollector.Collect("test", compilation);
    }
}
