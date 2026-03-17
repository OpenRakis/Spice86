namespace Spice86.Tests.Dos;

using FluentAssertions;

using Xunit;

using static BatchTestHelpers;

public class KeyboardInt16EnhancedIntegrationTests {
    [Fact]
    public void Int16Ah10h_ReturnsExtendedKeyScanCode() {
        WithTempDirectory("dos_int16_ah10", tempDir => {
            // Arrange: AH=10h (enhanced get keystroke), copy returned scan code (AH) into AL.
            CreateBinaryFile(tempDir, "READ10.COM", new byte[] {
                0xB4, 0x10,                                                     // MOV AH, 10h
                0xCD, 0x16,                                                     // INT 16h
                0x88, 0xE0,                                                     // MOV AL, AH
                0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
                0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
                0x8E, 0xC3,                                                     // MOV ES, BX
                0xBF, 0x00, 0x00,                                               // MOV DI, 0000h
                0xAB,                                                           // STOSW
                0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
                0xCD, 0x21                                                      // INT 21h
            });

            // F1 in BIOS keycode form: scan=0x3B, ascii=0x00 => 0x3B00
            ushort[] keys = new ushort[] { 0x3B00 };

            // Act
            byte[] bytes = RunWithPreloadedKeysAndCaptureVideoBytes(
                executablePath: tempDir + "\\READ10.COM",
                cDrivePath: tempDir,
                byteCount: 1,
                keyCodes: keys);

            // Assert
            bytes[0].Should().Be(0x3B);
        });
    }

    [Fact]
    public void Int16Ah11h_ReturnsExtendedKeyScanCodeWithoutDequeuingUnexpectedData() {
        WithTempDirectory("dos_int16_ah11", tempDir => {
            // Arrange: AH=11h (enhanced keystroke status), copy returned scan code (AH) into AL.
            CreateBinaryFile(tempDir, "READ11.COM", new byte[] {
                0xB4, 0x11,                                                     // MOV AH, 11h
                0xCD, 0x16,                                                     // INT 16h
                0x88, 0xE0,                                                     // MOV AL, AH
                0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
                0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
                0x8E, 0xC3,                                                     // MOV ES, BX
                0xBF, 0x00, 0x00,                                               // MOV DI, 0000h
                0xAB,                                                           // STOSW
                0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
                0xCD, 0x21                                                      // INT 21h
            });

            // F2 in BIOS keycode form: scan=0x3C, ascii=0x00 => 0x3C00
            ushort[] keys = new ushort[] { 0x3C00 };

            // Act
            byte[] bytes = RunWithPreloadedKeysAndCaptureVideoBytes(
                executablePath: tempDir + "\\READ11.COM",
                cDrivePath: tempDir,
                byteCount: 1,
                keyCodes: keys);

            // Assert
            bytes[0].Should().Be(0x3C);
        });
    }
}
