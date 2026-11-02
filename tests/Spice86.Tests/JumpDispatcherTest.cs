namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class JumpDispatcherTest {
    [Fact]
    public void ATargetThatThrewIsInvokedAgainOnTheNextJump() {
        // Arrange
        JumpDispatcher dispatcher = new();
        Action fixedAction = () => { };
        int invocations = 0;
        Func<int, Action> target = _ => {
            invocations++;
            if (invocations == 1) {
                throw new CallStackUnwindException(new SegmentedAddress(0x1000, 0x0010), "unwind");
            }
            return fixedAction;
        };

        // Act & Assert: the first jump lets the exception escape
        Action firstJump = () => dispatcher.Jump(target, 0);
        firstJump.Should().Throw<CallStackUnwindException>();

        // Act: the target is not on the jump stack anymore, so it runs again
        bool secondJump = dispatcher.Jump(target, 0);

        // Assert
        secondJump.Should().BeFalse();
        invocations.Should().Be(2);
        dispatcher.JumpAsmReturn.Should().BeSameAs(fixedAction);
    }
}
