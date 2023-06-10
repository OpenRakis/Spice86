namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

/// <summary>
///     Emulates the VGA Graphics registers.
/// </summary>
public sealed class GraphicsControllerRegisters {
    public GraphicsControllerRegister AddressRegister { get; set; }

    /// <summary>
    ///     Gets the Set/Reset register.
    /// </summary>
    public Register8 SetReset { get; } = new();

    /// <summary>
    ///     Gets the Enable Set/Reset register.
    /// </summary>
    public Register8 EnableSetReset { get; } = new();

    /// <summary>
    ///     Gets the Color Compare register.
    /// </summary>
    public byte ColorCompare { get; private set; }

    /// <summary>
    ///     Gets the Data Rotate register.
    /// </summary>
    public DataRotateRegister DataRotateRegister { get; } = new();

    /// <summary>
    ///     Gets the Read Map Select register.
    /// </summary>
    public ReadMapSelectRegister ReadMapSelectRegister { get; } = new();

    /// <summary>
    ///     Gets or sets the Graphics Mode register.
    /// </summary>
    public GraphicsModeRegister GraphicsModeRegister { get; } = new();

    /// <summary>
    ///     Gets or sets the Miscellaneous Graphics register.
    /// </summary>
    public MiscellaneousGraphicsRegister MiscellaneousGraphicsRegister { get; } = new();

    /// <summary>
    ///     Gets the Color Don't Care register.
    /// </summary>
    public byte ColorDontCare { get; private set; }

    /// <summary>
    ///     Gets or sets the Bit Mask register.
    /// </summary>
    public byte BitMask { get; set; }

    /// <summary>
    ///     Returns the current value of a graphics register.
    /// </summary>
    /// <param name="address">Address of register to read.</param>
    /// <returns>Current value of the register.</returns>
    public byte ReadRegister(GraphicsControllerRegister address) {
        return address switch {
            GraphicsControllerRegister.SetReset => SetReset.Value,
            GraphicsControllerRegister.EnableSetReset => EnableSetReset.Value,
            GraphicsControllerRegister.ColorCompare => ColorCompare,
            GraphicsControllerRegister.DataRotate => DataRotateRegister.Value,
            GraphicsControllerRegister.ReadMapSelect => ReadMapSelectRegister.Value,
            GraphicsControllerRegister.GraphicsMode => GraphicsModeRegister.Value,
            GraphicsControllerRegister.MiscellaneousGraphics => MiscellaneousGraphicsRegister.Value,
            GraphicsControllerRegister.ColorDontCare => ColorDontCare,
            GraphicsControllerRegister.BitMask => BitMask,
            _ => 0
        };
    }

    /// <summary>
    ///     Writes to a graphics register.
    /// </summary>
    /// <param name="address">Address of register to write.</param>
    /// <param name="value">Value to write to register.</param>
    public void Write(GraphicsControllerRegister address, byte value) {
        switch (address) {
            case GraphicsControllerRegister.SetReset:
                SetReset.Value = value;
                break;

            case GraphicsControllerRegister.EnableSetReset:
                EnableSetReset.Value = value;
                break;

            case GraphicsControllerRegister.ColorCompare:
                ColorCompare = value;
                break;

            case GraphicsControllerRegister.DataRotate:
                DataRotateRegister.Value = value;
                break;

            case GraphicsControllerRegister.ReadMapSelect:
                ReadMapSelectRegister.Value = value;
                break;

            case GraphicsControllerRegister.GraphicsMode:
                GraphicsModeRegister.Value = value;
                break;

            case GraphicsControllerRegister.MiscellaneousGraphics:
                MiscellaneousGraphicsRegister.Value = value;
                break;

            case GraphicsControllerRegister.ColorDontCare:
                ColorDontCare = value;
                break;

            case GraphicsControllerRegister.BitMask:
                BitMask = value;
                break;
        }
    }
}