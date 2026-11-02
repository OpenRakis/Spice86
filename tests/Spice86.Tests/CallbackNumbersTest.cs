namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Shared.Emulator.Memory;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

public class CallbackNumbersTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmulatorCallbacksHaveTheirFixedNumbers(bool enableXms) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2", installInterruptVectors: true, enableXms: enableXms);
        using Spice86DependencyInjection res = creator.Create();
        Dictionary<ushort, SegmentedAddress> actual = res.Machine.CallbackHandler.AllRunnables
            .Where(callback => callback.Index >= 0x100)
            .ToDictionary(callback => callback.Index, callback => callback.InstructionAddress);
        Dictionary<ushort, SegmentedAddress> expected = ExpectedCallbackAddresses();
        if (enableXms) {
            expected.Add(CallbackNumbers.XmsMultiplex, new SegmentedAddress(0xC8AB, 0x0016));
        }

        // Assert
        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void DefaultIrqNumbersCoverExactlyTheInstalledIrqs() {
        // Arrange
        Dictionary<byte, ushort> expected = new() {
            { 3, CallbackNumbers.DefaultIrq3 },
            { 4, CallbackNumbers.DefaultIrq4 },
            { 5, CallbackNumbers.DefaultIrq5 },
            { 7, CallbackNumbers.DefaultIrq7 },
            { 10, CallbackNumbers.DefaultIrq10 },
            { 11, CallbackNumbers.DefaultIrq11 }
        };

        // Act
        Dictionary<byte, ushort> actual = expected.Keys
            .ToDictionary(irq => irq, CallbackNumbers.ForDefaultIrq);

        // Assert
        actual.Should().BeEquivalentTo(expected);
        Action forIrqWithoutHandler = () => CallbackNumbers.ForDefaultIrq(0);
        forIrqWithoutHandler.Should().Throw<ArgumentOutOfRangeException>();
        Action forCascadeIrq = () => CallbackNumbers.ForDefaultIrq(6);
        forCascadeIrq.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Dictionary<ushort, SegmentedAddress> ExpectedCallbackAddresses() {
        return new Dictionary<ushort, SegmentedAddress> {
            [CallbackNumbers.TimerAfterInt8Execution] = new SegmentedAddress(0xF000, 0x000F),
            [CallbackNumbers.KeyboardHasKey] = new SegmentedAddress(0xF000, 0x0045),
            [CallbackNumbers.KeyboardDequeueAndSetAx] = new SegmentedAddress(0xF000, 0x004E),
            [CallbackNumbers.DefaultIrq3] = new SegmentedAddress(0xF000, 0x006D),
            [CallbackNumbers.DefaultIrq4] = new SegmentedAddress(0xF000, 0x0072),
            [CallbackNumbers.DefaultIrq5] = new SegmentedAddress(0xF000, 0x0077),
            [CallbackNumbers.DefaultIrq7] = new SegmentedAddress(0xF000, 0x007C),
            [CallbackNumbers.DefaultIrq10] = new SegmentedAddress(0xF000, 0x0081),
            [CallbackNumbers.DefaultIrq11] = new SegmentedAddress(0xF000, 0x0086),
            [CallbackNumbers.DosInt21CheckStdinReady] = new SegmentedAddress(0xF000, 0x00AB),
            [CallbackNumbers.DosInt21StdinInputDispatch] = new SegmentedAddress(0xF000, 0x00BB),
            [CallbackNumbers.MouseBeforeUserHandler] = new SegmentedAddress(0xF000, 0x00E9),
            [CallbackNumbers.MouseAfterUserHandler] = new SegmentedAddress(0xF000, 0x00F2)
        };
    }
}
