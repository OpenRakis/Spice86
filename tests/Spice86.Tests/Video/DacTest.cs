namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;

using Xunit;

public class DacTest {
    [Fact]
    public void TestReadingDacReturns6BitData() {
        // Arrange
        var dac = new DacRegisters();

        // Act
        for (byte i = 0; i < byte.MaxValue; i++) {
            dac.DataRegister = i;
        }
        dac.IndexRegisterReadMode = 0;

        // Assert
        for (byte i = 0; i < byte.MaxValue; i++) {
            byte expected = (byte)(i & 0b00111111);
            byte result = dac.DataRegister;
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void WhiteStaysWhite() {
        // Arrange
        var dac = new DacRegisters {
            // Act
            DataRegister = 0b111111
        };

        dac.DataRegister = 0b111111;
        dac.DataRegister = 0b111111;

        // Assert – both the legacy ArgbPalette view and the new fast PaletteMap must agree
        Assert.Equal(0xFFFFFFFF, dac.ArgbPalette[0]);
        Assert.Equal(0xFFFFFFFF, dac.PaletteMap[0]);
    }

    [Fact]
    public void PaletteMapRespectsPixelMask() {
        // Arrange – write red (63,0,0) at index 1, black (0,0,0) at index 0
        var dac = new DacRegisters();
        // Index 1: red
        dac.IndexRegisterWriteMode = 1;
        dac.DataRegister = 63; // R
        dac.DataRegister = 0;  // G
        dac.DataRegister = 0;  // B

        // With PixelMask=0xFF every index maps to itself
        Assert.Equal(0xFFFF0000, dac.PaletteMap[1]); // red

        // With PixelMask=0xFE index 1 is masked to index 0 (black)
        dac.PixelMask = 0xFE;
        Assert.Equal(0xFF000000, dac.PaletteMap[1]); // black (palette[0])

        // Restoring mask restores the mapping
        dac.PixelMask = 0xFF;
        Assert.Equal(0xFFFF0000, dac.PaletteMap[1]); // red again
    }

    [Fact]
    public void AttributeMapReflectsDacChanges() {
        // Arrange – identity InternalPalette (entry i maps to DAC index i) with default ColorSelect
        var dac = new DacRegisters();
        var attr = new AttributeControllerRegisters();

        // Set InternalPalette[5] = 5 (identity; default is 0, so set explicitly)
        attr.WriteRegister((AttributeControllerRegister)5, 5);

        // Write green (0,63,0) to DAC index 5
        dac.IndexRegisterWriteMode = 5;
        dac.DataRegister = 0;  // R
        dac.DataRegister = 63; // G
        dac.DataRegister = 0;  // B
        // PaletteMap[5] is now green; rebuild AttributeMap to pick it up
        dac.RebuildAttributeMap(attr);

        Assert.Equal(0xFF00FF00, dac.AttributeMap[5]);

        // Changing the DAC colour should be reflected after rebuild
        dac.IndexRegisterWriteMode = 5;
        dac.DataRegister = 0;  // R
        dac.DataRegister = 0;  // G
        dac.DataRegister = 63; // B
        dac.RebuildAttributeMap(attr);

        Assert.Equal(0xFF0000FF, dac.AttributeMap[5]);
    }
}