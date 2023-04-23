namespace Spice86.Core.Emulator.Devices.Video;

public interface IVgaInterrupts {
    void WriteString();
    void SetVideoMode();
    VideoFunctionalityInfo GetFunctionalityInfo();
    void GetSetDisplayCombinationCode();
    void VideoSubsystemConfiguration();
    void LoadFontInfo();
    void SetPaletteRegisters();
    void GetVideoState();
    void WriteTextInTeletypeMode();
    void SetColorPaletteOrBackGroundColor();
    void WriteCharacterAtCursor();
    void WriteCharacterAndAttributeAtCursor();
    void ReadCharacterAndAttributeAtCursor();
    void ScrollPageDown();
    void ScrollPageUp();
    void SelectActiveDisplayPage();
    void GetCursorPosition();
    void SetCursorPosition();
    void SetCursorType();
    void WriteDot();
    void ReadDot();
}