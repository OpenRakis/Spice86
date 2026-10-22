namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

using System.Reflection;

using Xunit;

/// <summary>
/// Runs every ALU method and checks that <see cref="AluFlagTable"/> describes exactly the flags it reads and
/// writes. A flag is written for an input when it is not left as it was with all flags clear and with all
/// flags set; a flag is read when setting it alone changes the result or another flag.
/// </summary>
public class AluFlagTableTest {
    private static readonly BitWidth[] Widths = [BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32];
    private static readonly StatusFlags[] EachFlag = [
        StatusFlags.Carry, StatusFlags.Parity, StatusFlags.Auxiliary, StatusFlags.Zero, StatusFlags.Sign, StatusFlags.Overflow
    ];
    private static readonly ulong[] EdgeValues = [
        0, 1, 2, 3, 0x0F, 0x10, 0x7F, 0x80, 0x81, 0xFF, 0x100, 0x7FFF, 0x8000, 0x8001, 0xFFFF,
        0x10000, 0x7FFFFFFF, 0x80000000, 0x80000001, 0xFFFFFFFF
    ];

    public static TheoryData<string, BitWidth> TwoOperandOperations =>
        Cases(["Add", "Adc", "Sub", "Sbb", "And", "Or", "Xor", "Mul", "Imul", "Div", "Idiv"], Widths);

    public static TheoryData<string, BitWidth> OneOperandOperations => Cases(["Inc", "Dec", "UpdateFlags"], Widths);

    public static TheoryData<string, BitWidth> CountOperations {
        get {
            TheoryData<string, BitWidth> cases = Cases(["Shl", "Shr", "Sar", "Rol", "Ror", "Rcl", "Rcr"], Widths);
            foreach (string operation in new[] { "Shld", "Shrd" }) {
                cases.Add(operation, BitWidth.WORD_16);
                cases.Add(operation, BitWidth.DWORD_32);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(TwoOperandOperations))]
    public void TwoOperandOperationMatchesTheAlu(string operation, BitWidth width) {
        // Act
        ObservedEffect observed = Observe(operation, width, Pairs(Values(width)).ToList());

        // Assert
        AssertTableMatches(operation, width, null, observed);
    }

    [Theory]
    [MemberData(nameof(OneOperandOperations))]
    public void OneOperandOperationMatchesTheAlu(string operation, BitWidth width) {
        // Act
        ObservedEffect observed = Observe(operation, width, Values(width).Select(value => new[] { value }).ToList());

        // Assert
        AssertTableMatches(operation, width, null, observed);
    }

    [Theory]
    [MemberData(nameof(CountOperations))]
    public void CountOperationMatchesTheAluForEveryCount(string operation, BitWidth width) {
        // Arrange
        IReadOnlyList<ulong[]> operands = operation is "Shld" or "Shrd"
            ? Pairs(Values(width)).ToList()
            : Values(width).Select(value => new[] { value }).ToList();
        StatusFlags readsForSomeCount = StatusFlags.None;
        StatusFlags writtenForSomeCount = StatusFlags.None;
        StatusFlags writtenForEveryCount = StatusFlags.All;

        for (ulong count = 0; count <= 33; count++) {
            // Act
            ulong currentCount = count;
            ObservedEffect observed = Observe(operation, width, operands.Select(values => values.Append(currentCount).ToArray()).ToList());

            // Assert
            AssertTableMatches(operation, width, count, observed);
            readsForSomeCount |= observed.Reads;
            writtenForSomeCount |= observed.Written;
            writtenForEveryCount &= observed.Written;
        }

        // Assert
        AluFlagTable.TryGetEffect(operation, width, null, out FlagEffect unknownCount).Should().BeTrue();
        unknownCount.Reads.Should().Be(readsForSomeCount);
        unknownCount.MayWrite.Should().Be(writtenForSomeCount);
        unknownCount.MustWrite.Should().Be(writtenForEveryCount);
    }

    [Fact]
    public void EveryPublicAluMethodHasAnEntry() {
        // Arrange
        IEnumerable<string> names = typeof(Alu16).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.DeclaringType != typeof(object))
            .Select(method => method.Name)
            .Distinct();

        // Assert
        foreach (string name in names) {
            AluFlagTable.TryGetEffect(name, BitWidth.WORD_16, 1, out _).Should().BeTrue(name);
        }
    }

    private static void AssertTableMatches(string operation, BitWidth width, ulong? count, ObservedEffect observed) {
        string because = $"{operation} {width} count {count}";
        AluFlagTable.TryGetEffect(operation, width, count, out FlagEffect expected).Should().BeTrue(because);
        expected.Reads.Should().Be(observed.Reads, because);
        expected.MustWrite.Should().Be(observed.Written, because);
        expected.MayWrite.Should().Be(observed.Written, because);
    }

    private static ObservedEffect Observe(string operation, BitWidth width, IReadOnlyList<ulong[]> inputs) {
        State state = new(CpuModel.INTEL_80386);
        object alu = CreateAlu(width, state);
        MethodInfo method = FindMethod(alu.GetType(), operation, inputs[0].Length);
        StatusFlags reads = StatusFlags.None;
        StatusFlags written = StatusFlags.None;
        foreach (ulong[] operands in inputs) {
            object?[] arguments = method.GetParameters().Select((parameter, index) => ToArgument(operands[index], parameter.ParameterType)).ToArray();
            Probe allClear = Run(state, alu, method, arguments, StatusFlags.None);
            Probe allSet = Run(state, alu, method, arguments, StatusFlags.All);
            if (allClear.Faulted || allSet.Faulted) {
                continue;
            }
            foreach (StatusFlags flag in EachFlag) {
                bool keptAsItWas = (allClear.Flags & flag) == StatusFlags.None && (allSet.Flags & flag) == flag;
                if (!keptAsItWas) {
                    written |= flag;
                }
                Probe onlyThisFlag = Run(state, alu, method, arguments, flag);
                bool resultChanged = onlyThisFlag.Result != allClear.Result;
                bool otherFlagChanged = ((onlyThisFlag.Flags ^ allClear.Flags) & ~flag) != StatusFlags.None;
                if (resultChanged || otherFlagChanged) {
                    reads |= flag;
                }
            }
        }
        return new ObservedEffect(reads, written);
    }

    private static Probe Run(State state, object alu, MethodInfo method, object?[] arguments, StatusFlags preset) {
        state.CarryFlag = (preset & StatusFlags.Carry) != StatusFlags.None;
        state.ParityFlag = (preset & StatusFlags.Parity) != StatusFlags.None;
        state.AuxiliaryFlag = (preset & StatusFlags.Auxiliary) != StatusFlags.None;
        state.ZeroFlag = (preset & StatusFlags.Zero) != StatusFlags.None;
        state.SignFlag = (preset & StatusFlags.Sign) != StatusFlags.None;
        state.OverflowFlag = (preset & StatusFlags.Overflow) != StatusFlags.None;
        try {
            object? result = method.Invoke(alu, arguments);
            return new Probe(ToUInt64(result), ReadStatusFlags(state), false);
        } catch (TargetInvocationException exception) when (exception.InnerException is CpuDivisionErrorException) {
            return new Probe(0, ReadStatusFlags(state), true);
        }
    }

    private static StatusFlags ReadStatusFlags(State state) =>
        (StatusFlags)(ushort)(state.Flags.FlagRegister & (uint)StatusFlags.All);

    private static object CreateAlu(BitWidth width, State state) => width switch {
        BitWidth.BYTE_8 => new Alu8(state),
        BitWidth.WORD_16 => new Alu16(state),
        BitWidth.DWORD_32 => new Alu32(state),
        _ => throw new ArgumentOutOfRangeException(nameof(width))
    };

    private static MethodInfo FindMethod(Type aluType, string operation, int arity) =>
        aluType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == operation && method.GetParameters().Length == arity);

    private static object ToArgument(ulong value, Type type) {
        unchecked {
            if (type == typeof(byte)) {
                return (byte)value;
            }
            if (type == typeof(sbyte)) {
                return (sbyte)value;
            }
            if (type == typeof(ushort)) {
                return (ushort)value;
            }
            if (type == typeof(short)) {
                return (short)value;
            }
            if (type == typeof(uint)) {
                return (uint)value;
            }
            if (type == typeof(int)) {
                return (int)value;
            }
            if (type == typeof(ulong)) {
                return value;
            }
            if (type == typeof(long)) {
                return (long)value;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(type), type, "Unexpected ALU parameter type.");
    }

    private static ulong ToUInt64(object? result) {
        unchecked {
            return result switch {
                null => 0,
                byte value => value,
                sbyte value => (ulong)value,
                ushort value => value,
                short value => (ulong)value,
                uint value => value,
                int value => (ulong)value,
                ulong value => value,
                long value => (ulong)value,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unexpected ALU result type.")
            };
        }
    }

    private static ulong[] Values(BitWidth width) {
        if (width == BitWidth.BYTE_8) {
            return Enumerable.Range(0, 256).Select(value => (ulong)value).ToArray();
        }
        ulong mask = width == BitWidth.WORD_16 ? 0xFFFFUL : 0xFFFFFFFFUL;
        Random random = new(86);
        IEnumerable<ulong> randomValues = Enumerable.Range(0, 40).Select(_ => (ulong)random.NextInt64(0, 1L << 32));
        return EdgeValues.Concat(randomValues).Select(value => value & mask).Distinct().ToArray();
    }

    private static IEnumerable<ulong[]> Pairs(ulong[] values) =>
        values.SelectMany(first => values.Select(second => new[] { first, second }));

    private static TheoryData<string, BitWidth> Cases(string[] operations, BitWidth[] widths) {
        TheoryData<string, BitWidth> cases = new();
        foreach (string operation in operations) {
            foreach (BitWidth width in widths) {
                cases.Add(operation, width);
            }
        }
        return cases;
    }

    private readonly record struct Probe(ulong Result, StatusFlags Flags, bool Faulted);

    private readonly record struct ObservedEffect(StatusFlags Reads, StatusFlags Written);
}
