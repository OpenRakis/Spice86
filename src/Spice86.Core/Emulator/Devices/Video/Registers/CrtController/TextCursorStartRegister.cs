namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

public class TextCursorStartRegister : Register8 {
    /// <summary>
    ///     If this bit is programmed to ‘1’, the text cursor is disabled (that is, it is not displayed). If this bit is
    ///     programmed to ‘0’, the text cursor functions normally.
    /// </summary>
    public bool DisableTextCursor {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    ///     This field specifies the scanline within the Character Cell where the text cursor is to start. If the Text
    ///     Cursor Start value is greater than the Text Cursor End value, there is no text cursor displayed. If the Text
    ///     Cursor Start value is equal to the Text Cursor End value, the text cursor occupies a single scanline.
    /// </summary>
    public byte TextCursorStart {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }
}