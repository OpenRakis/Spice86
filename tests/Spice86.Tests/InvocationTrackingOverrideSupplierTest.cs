namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// The wrapper re-creates every registration of the supplier it wraps, so each override's dispatch mode
/// must survive the wrapping. Losing it would silently turn every generated run into a permissive one
/// that hands control back to the interpreter.
/// </summary>
public class InvocationTrackingOverrideSupplierTest {
    private static readonly SegmentedAddress StrictAddress = new(0x1000, 0x0010);
    private static readonly SegmentedAddress PermissiveAddress = new(0x2000, 0x0020);

    [Fact]
    public void TheWrappedRegistrationsKeepTheirInterpreterFallbackMode() {
        // Arrange: one strict and one permissive registration
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        InvocationTrackingOverrideSupplier wrapper = new(new TwoModeOverrideSupplier());
        Configuration configuration = new() { HttpApiPort = 0 };

        // Act
        IDictionary<SegmentedAddress, FunctionInformation> wrapped =
            wrapper.GenerateFunctionInformations(Substitute.For<ILogger>(), configuration, 0x1000, machine);

        // Assert
        wrapped[StrictAddress].ForbidsInterpreterFallback.Should().BeTrue();
        wrapped[PermissiveAddress].ForbidsInterpreterFallback.Should().BeFalse();
    }

    private sealed class TwoModeOverrideSupplier : IOverrideSupplier {
        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [StrictAddress] = new FunctionInformation(StrictAddress, "strict", _ => () => { }) {
                    ForbidsInterpreterFallback = true
                },
                [PermissiveAddress] = new FunctionInformation(PermissiveAddress, "permissive", _ => () => { })
            };
        }
    }
}
