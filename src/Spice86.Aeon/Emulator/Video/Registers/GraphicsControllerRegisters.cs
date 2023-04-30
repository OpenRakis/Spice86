namespace Spice86.Aeon.Emulator.Video.Registers;

using Spice86.Aeon.Emulator.Video.Registers.Graphics;

/// <summary>
/// Emulates the VGA Graphics registers.
/// </summary>
public sealed class GraphicsControllerRegisters {
    /// <summary>
    /// Gets the Set/Reset register.
    /// </summary>
    public MaskValue SetReset { get; private set; }

    /// <summary>
    /// Gets the Enable Set/Reset register.
    /// </summary>
    public MaskValue EnableSetReset { get; private set; }

    /// <summary>
    /// Gets the Color Compare register.
    /// </summary>
    public byte ColorCompare { get; private set; }

    /// <summary>
    /// Gets the Data Rotate register.
    /// </summary>
    public DataRotateRegister DataRotateRegister { get; } = new();

    /// <summary>
    /// Gets the Read Map Select register.
    /// </summary>
    public ReadMapSelectRegister ReadMapSelectRegister { get; } = new();

    /// <summary>
    /// Gets or sets the Graphics Mode register.
    /// </summary>
    public GraphicsModeRegister GraphicsModeRegister { get; } = new();

    /// <summary>
    /// Gets or sets the Miscellaneous Graphics register.
    /// </summary>
    public MiscellaneousGraphicsRegister MiscellaneousGraphicsRegister { get; } = new();

    /// <summary>
    /// Gets the Color Don't Care register.
    /// </summary>
    public byte ColorDontCare { get; private set; }

    /// <summary>
    /// Gets or sets the Bit Mask register.
    /// </summary>
    public byte BitMask { get; set; }

    /// <summary>
    /// Returns the current value of a graphics register.
    /// </summary>
    /// <param name="address">Address of register to read.</param>
    /// <returns>Current value of the register.</returns>
    public byte ReadRegister(GraphicsRegister address) {
        return address switch {
            GraphicsRegister.SetReset => SetReset.Packed,
            GraphicsRegister.EnableSetReset => EnableSetReset.Packed,
            GraphicsRegister.ColorCompare => ColorCompare,
            GraphicsRegister.DataRotate => DataRotateRegister.Value,
            GraphicsRegister.ReadMapSelect => ReadMapSelectRegister.Value,
            GraphicsRegister.GraphicsMode => GraphicsModeRegister.Value,
            GraphicsRegister.MiscellaneousGraphics => MiscellaneousGraphicsRegister.Value,
            GraphicsRegister.ColorDontCare => ColorDontCare,
            GraphicsRegister.BitMask => BitMask,
            _ => 0
        };
    }

    /// <summary>
    /// Writes to a graphics register.
    /// </summary>
    /// <param name="address">Address of register to write.</param>
    /// <param name="value">Value to write to register.</param>
    public void Write(GraphicsRegister address, byte value) {
        switch (address) {
            case GraphicsRegister.SetReset:
                SetReset = value;
                break;

            case GraphicsRegister.EnableSetReset:
                EnableSetReset = value;
                break;

            case GraphicsRegister.ColorCompare:
                ColorCompare = value;
                break;

            case GraphicsRegister.DataRotate:
                DataRotateRegister.Value = value;
                break;

            case GraphicsRegister.ReadMapSelect:
                ReadMapSelectRegister.Value = value;
                break;

            case GraphicsRegister.GraphicsMode:
                GraphicsModeRegister.Value = value;
                break;

            case GraphicsRegister.MiscellaneousGraphics:
                MiscellaneousGraphicsRegister.Value = value;
                break;

            case GraphicsRegister.ColorDontCare:
                ColorDontCare = value;
                break;

            case GraphicsRegister.BitMask:
                BitMask = value;
                break;
        }
    }
}