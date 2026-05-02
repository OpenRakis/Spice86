namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Xunit;

public class BiosKeyboardInt9HandlerIntegrationTests {
    [Fact]
    public void Int09_IgnoresStalePort60ValueWhenOutputBufferIsEmpty() {
        using Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();

        Intel8042Controller keyboardController = spice86.Machine.KeyboardController;
        BiosKeyboardInt9Handler int9Handler = spice86.Machine.BiosKeyboardInt9Handler;
        BiosKeyboardBuffer keyboardBuffer = int9Handler.BiosKeyboardBuffer;

        DrainControllerOutputBuffer(keyboardController);
        keyboardBuffer.Flush();

        keyboardController.AddKeyboardByte(0x00);
        byte seededValue = keyboardController.ReadByte(KeyboardPorts.Data);
        seededValue.Should().Be(0x00);

        byte statusAfterRead = keyboardController.ReadByte(KeyboardPorts.StatusRegister);
        (statusAfterRead & (byte)Intel8042Controller.StatusBits.OutputBufferFull).Should().Be(0,
            "test setup requires an empty output buffer before triggering INT09");

        int9Handler.Run();

        keyboardBuffer.PeekKeyCode().Should().BeNull(
            "INT09 must ignore stale port 0x60 data when the 8042 output buffer has no new byte");
    }

    private static void DrainControllerOutputBuffer(Intel8042Controller keyboardController) {
        while ((keyboardController.ReadByte(KeyboardPorts.StatusRegister) &
                (byte)Intel8042Controller.StatusBits.OutputBufferFull) != 0) {
            keyboardController.ReadByte(KeyboardPorts.Data);
        }
    }
}