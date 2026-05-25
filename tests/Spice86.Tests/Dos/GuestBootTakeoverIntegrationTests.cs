namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Shared.Utils;

using System.IO;

using Xunit;

using static BatchTestHelpers;

/// <summary>
/// Verifies that boot targets take over execution instead of continuing to run on top of Spice86 DOS services.
/// </summary>
public class GuestBootTakeoverIntegrationTests
{
    [Fact]
    public void BootFloppy_MarksDosAsReplacedByGuestBoot()
    {
        WithTempFile("boot_floppy_takes_over_dos", tempDir =>
        {
            string imagePath = Path.Join(tempDir, "TAKEOVER.IMG");
            string startBatchPath = Path.Join(tempDir, "START.BAT");
            File.WriteAllBytes(imagePath, BuildBootSectorImageThatTriesDosOutput('D', 'O'));
            File.WriteAllText(startBatchPath, "IMGMOUNT A C:\\TAKEOVER.IMG -t floppy\r\nBOOT -l A\r\n");

            using Spice86Creator creator = new Spice86Creator(
                binName: startBatchPath,
                enablePit: true,
                maxCycles: 300000,
                installInterruptVectors: true,
                cDrive: tempDir);
            using Spice86DependencyInjection spice86 = creator.Create();

            spice86.ProgramExecutor.Run();

            spice86.Machine.Dos.ProcessManager.IsGuestBooted.Should().BeTrue(
                "BOOT should mark the DOS session as replaced before guest code starts");

            char[] cells = new char[2];
            for (int i = 0; i < cells.Length; i++)
            {
                uint videoAddress = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)(i * 2));
                cells[i] = (char)spice86.Machine.Memory.UInt8[videoAddress];
            }

            spice86.Machine.Dos.ProcessManager.CurrentProgramName.Should().BeEmpty(
                "guest boot should clear DOS-visible program tracking just like DOSBox-Staging clears launched program names");
            cells[1].Should().Be('O', "the boot sector should still execute after BOOT hands control to the guest image");
        });
    }

    [Fact]
    public void BiosBinary_LoadMarksDosAsReplacedByGuestBoot()
    {
        WithTempFile("boot_bios_takes_over_dos", tempDir =>
        {
            string biosPath = Path.Join(tempDir, "TAKEOVER.BIN");
            File.WriteAllBytes(biosPath, new byte[] { 0xF4 });

            using Spice86Creator creator = new Spice86Creator(
                binName: biosPath,
                enablePit: true,
                maxCycles: 300000,
                installInterruptVectors: true,
                cDrive: tempDir);
            using Spice86DependencyInjection spice86 = creator.Create();

            spice86.Machine.Dos.ProcessManager.IsGuestBooted.Should().BeTrue(
                "loading a BIOS binary should immediately mark DOS as replaced");
            spice86.Machine.Dos.ProcessManager.CurrentProgramName.Should().BeEmpty(
                "guest BIOS takeover should clear DOS-visible current program tracking");
        });
    }

    private static byte[] BuildBootSectorImageThatTriesDosOutput(char dosCharacter, char directCharacter)
    {
        byte[] image = new byte[1440 * 1024];
        byte[] bootSector = new byte[] {
            0xB4, 0x02,             // MOV AH, 02h
            0xB2, (byte)dosCharacter,
            0xCD, 0x21,             // INT 21h
            0xB8, 0x00, 0xB8,       // MOV AX, B800h
            0x8E, 0xC0,             // MOV ES, AX
            0xBF, 0x02, 0x00,       // MOV DI, 2 (second text cell)
            0xB0, (byte)directCharacter,
            0xB4, 0x07,
            0xAB,                   // STOSW
            0xF4                    // HLT
        };

        for (int i = 0; i < bootSector.Length; i++)
        {
            image[i] = bootSector[i];
        }

        image[510] = 0x55;
        image[511] = 0xAA;
        return image;
    }
}