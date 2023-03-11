namespace Spice86.Core.Emulator.Devices.Video;

public interface IVgaInterrupts {
    void WriteString();
    void SetVideoMode();
    VideoFunctionalityInfo GetFunctionalityInfo();
    void VideoDisplayCombination();
    void VideoSubsystemConfiguration();
    void CharacterGeneratorRoutine();
    void GetSetPaletteRegisters();
    void GetVideoMode();
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
}