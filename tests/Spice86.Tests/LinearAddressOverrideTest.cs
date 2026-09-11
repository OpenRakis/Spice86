using Spice86.Core.CLI;

namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Verifies linear-address override registration
/// (<see cref="CSharpOverrideHelper.DefineFunction(uint, Func{int, Action}, bool, string?)"/> /
/// <see cref="CSharpOverrideHelper.SearchFunctionOverride"/>): an override registered by flat linear
/// address keeps firing after a GDT descriptor edit repoints a DIFFERENT selector's base to alias the
/// same linear address.
/// </summary>
public class LinearAddressOverrideTest {
    private readonly ILogger _loggerServiceMock = Substitute.For<ILogger>();

    [Fact]
    public void LinearOverride_SurvivesDescriptorEditRepointingADifferentSelector() {
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;

        const uint gdtBase = 0x600;
        const ushort selectorA = 0x08;
        const ushort selectorB = 0x10;
        const ushort offset = 0x0050;
        const uint linearAddress = 0x1000 + offset;

        machine.CpuState.ControlRegisters.Cr0 = 1; // PE=1, enter protected mode
        machine.CpuState.Gdtr.Base = gdtBase;
        machine.CpuState.Gdtr.Limit = 0x17; // 3 entries (null, A, B)
        WriteCodeDescriptor(machine, gdtBase, selectorA, @base: 0x1000);
        WriteCodeDescriptor(machine, gdtBase, selectorB, @base: 0x2000);

        LinearOverrideProbe probe = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, _loggerServiceMock, new Configuration { HttpApiPort = 0 });
        probe.DefineFunction(linearAddress, probe.TargetFunction, name: "TargetFunction");

        probe.SearchFunctionOverride(new SegmentedAddress(selectorA, offset)).Should().NotBeNull();
        probe.SearchFunctionOverride(new SegmentedAddress(selectorB, offset)).Should().BeNull();

        // Repoint selector B's base to alias the same linear address as selector A, as if a running
        // protected-mode program edited its own GDT.
        WriteCodeDescriptor(machine, gdtBase, selectorB, @base: 0x1000);

        Func<int, Action>? foundViaB = probe.SearchFunctionOverride(new SegmentedAddress(selectorB, offset));
        foundViaB.Should().NotBeNull();
        foundViaB!(0);
        probe.TargetFunctionCalled.Should().Be(1);
    }

    private static void WriteCodeDescriptor(Machine machine, uint gdtBase, ushort selector, uint @base) {
        uint entryOffset = gdtBase + (uint)(selector >> 3) * 8u;
        machine.Memory.UInt16[entryOffset] = 0xFFFF; // limit_low
        machine.Memory.UInt16[entryOffset + 2] = (ushort)@base; // base_low
        machine.Memory[entryOffset + 4] = (byte)(@base >> 16); // base_mid
        machine.Memory[entryOffset + 5] = 0x9A; // present, DPL0, code, executable, readable
        machine.Memory[entryOffset + 6] = 0x00; // no granularity/big flags
        machine.Memory[entryOffset + 7] = (byte)(@base >> 24); // base_high
    }
}

class LinearOverrideProbe : CSharpOverrideHelper {
    public int TargetFunctionCalled { get; private set; }

    public LinearOverrideProbe(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration)
        : base(functionInformations, machine, loggerService, configuration) {
    }

    public Action TargetFunction(int loadOffset) {
        TargetFunctionCalled++;
        return NearRet();
    }
}
