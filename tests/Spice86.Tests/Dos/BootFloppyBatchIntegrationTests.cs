namespace Spice86.Tests.Dos;

using FluentAssertions;

using System.IO;

using Xunit;

using static BatchTestHelpers;

/// <summary>
/// End-to-end floppy-image batch integration tests covering the live IMGMOUNT and BOOT path.
/// </summary>
public class BootFloppyBatchIntegrationTests {
    [Fact]
    public void BatchEngine_ImgMountAndBootFloppy_WithDefaultMaximumTiming_ExecutesBootSector() {
        WithTempFile("boot_imgmount_floppy", tempDir => {
            // Arrange: Spice86Creator uses the normal DI defaults, so this covers the retained
            // DOSBox-compatible default floppy speed of maximum while exercising the real batch path.
            string imagePath = Path.Join(tempDir, "PARITY.IMG");
            string startBatchPath = Path.Join(tempDir, "START.BAT");
            File.WriteAllBytes(imagePath, BuildBootSectorImage('P'));
            File.WriteAllText(startBatchPath, "IMGMOUNT A C:\\PARITY.IMG -t floppy\r\nBOOT -l A\r\n");

            // Act
            char actual = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            actual.Should().Be('P');
        });
    }

    private static byte[] BuildBootSectorImage(char value) {
        byte[] image = new byte[1440 * 1024];
        byte[] bootSector = new byte[] {
            0xB8, 0x00, 0xB8,       // MOV AX, B800h
            0x8E, 0xC0,             // MOV ES, AX
            0x31, 0xFF,             // XOR DI, DI
            0xB0, (byte)value,      // MOV AL, value
            0xB4, 0x07,             // MOV AH, 07h
            0xAB,                   // STOSW
            0xF4                    // HLT
        };

        for (int i = 0; i < bootSector.Length; i++) {
            image[i] = bootSector[i];
        }

        image[510] = 0x55;
        image[511] = 0xAA;
        return image;
    }
}