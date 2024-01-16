namespace Spice86.Core.Emulator.Devices.Video.Registers.AttributeController;

/// <summary>
/// Represents the 8 bit Attribute Controller Mode register.
/// </summary>
public class AttributeControllerModeRegister : Register8 {
    /// <summary>
    ///     True: LUT bit 4 and 5 come from Color Select register
    ///     False: LUT bit 4 and 5 come from Palette entry
    /// </summary>
    public bool VideoOutput45Select {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    ///     256 Color Output Assembler
    ///     False: 6 bits of video (translated from 4 bits by the internal color palette) are output every dot clock
    ///     True: Two 4-bit sets of video data are assembled to generate a-bit video data at half the frequency of the
    ///     internal dot clock (256 color mode).
    /// </summary>
    public bool PixelWidth8 {
        get => GetBit(6);
        set => SetBit(6, value);
    }

    /// <summary>
    ///     False: pan both top and bottom half of split screen
    ///     True: pan only top half of split screen
    /// </summary>
    public bool PixelPanningCompatibility {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    ///     False: Disable Blinking and enable text mode background intensity
    ///     True: Enable the blink attribute in text and graphics modes.
    /// </summary>
    public bool BlinkingEnabled {
        get => GetBit(3);
        set => SetBit(3, value);
    }

    /// <summary>
    ///     Enable Line Graphics Character Codes. This bit is dependent on bit 0 of the Override register.
    ///     False: Make the ninth pixel appear the same as the background
    ///     True: For special line graphics character codes (OCOh-ODFh), make the ninth pixel identical to the eighth
    ///     pixel of the character. For other characters, the ninth pixel is the same as the background.
    /// </summary>
    public bool LineGraphicsEnabled {
        get => GetBit(2);
        set => SetBit(2, value);
    }

    /// <summary>
    ///     True: character attributes are mono attributes
    ///     False: character attributes are colors
    /// </summary>
    public bool MonochromeEmulation {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    ///     Switch between text and graphics modes
    /// </summary>
    public bool GraphicsMode {
        get => GetBit(0);
        set => SetBit(0, value);
    }
}