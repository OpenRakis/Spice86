namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class VideoCardInfo : ObservableObject {
    [ObservableProperty] private byte _dacReadIndex;

    [ObservableProperty] private byte _dacWriteIndex;

    [ObservableProperty] private byte _graphicsBitMask;
    
    [ObservableProperty] private byte _graphicsColorCompare;

    [ObservableProperty] private byte _graphicsGraphicsMode;
    
    [ObservableProperty] private byte _graphicsMiscellaneousGraphics;
    
    [ObservableProperty] private uint _graphicsSetResetExpanded;
    
    [ObservableProperty] private uint _graphicsColorDontCareExpanded;
    
    [ObservableProperty] private uint _graphicsEnableSetResetExpanded;
    
    [ObservableProperty] private byte _graphicsReadMapSelect;
    
    [ObservableProperty] private byte _sequencerReset;
    
    [ObservableProperty] private byte _sequencerClockingMode;
    
    [ObservableProperty] private uint _sequencerMapMaskExpanded;
    
    [ObservableProperty] private byte _sequencerCharacterMapSelect;
    
    [ObservableProperty] private byte _sequencerSequencerMemoryMode;
    
    [ObservableProperty] private byte _attributeControllerColorSelect;
    
    [ObservableProperty] private byte _attributeControllerOverscanColor;
    
    [ObservableProperty] private byte _attributeControllerAttributeModeControl;
    
    [ObservableProperty] private byte _attributeControllerColorPlaneEnable;
    
    [ObservableProperty] private byte _attributeControllerHorizontalPixelPanning;
    
    [ObservableProperty] private byte _crtControllerOffset;
    
    [ObservableProperty] private byte _crtControllerOverflow;
    
    [ObservableProperty] private byte _crtControllerCursorEnd;
    
    [ObservableProperty] private ushort _crtControllerCursorLocation;
    
    [ObservableProperty] private byte _crtControllerCursorStart;
    
    [ObservableProperty] private byte _crtControllerHorizontalTotal;
    
    [ObservableProperty] private byte _crtControllerLineCompare;
    
    [ObservableProperty] private ushort _crtControllerStartAddress;
    
    [ObservableProperty] private byte _crtControllerUnderlineLocation;
    
    [ObservableProperty] private byte _crtControllerVerticalTotal;
    
    [ObservableProperty] private byte _crtControllerCrtModeControl;
    
    [ObservableProperty] private byte _crtControllerEndHorizontalBlanking;
    
    [ObservableProperty] private byte _crtControllerEndHorizontalDisplay;
    
    [ObservableProperty] private byte _crtControllerEndHorizontalRetrace;
    
    [ObservableProperty] private byte _crtControllerEndVerticalBlanking;
    
    [ObservableProperty] private byte _crtControllerMaximumScanLine;
    
    [ObservableProperty] private byte _crtControllerPresetRowScan;
    
    [ObservableProperty] private byte _crtControllerStartHorizontalBlanking;
    
    [ObservableProperty] private byte _crtControllerStartHorizontalRetrace;
    
    [ObservableProperty] private byte _crtControllerStartVerticalBlanking;
    
    [ObservableProperty] private byte _crtControllerVerticalDisplayEnd;
    
    [ObservableProperty] private byte _crtControllerVerticalRetraceEnd;
    
    [ObservableProperty] private byte _crtControllerVerticalRetraceStart;
    
    [ObservableProperty] private int _currentModeHeight;

    [ObservableProperty] private int _currentModeWidth;
    
    [ObservableProperty] private int _currentModeStride;
    
    [ObservableProperty] private int _currentModeBytePanning;
    
    [ObservableProperty] private int _currentModeFontHeight;
    
    [ObservableProperty] private int _currentModeHorizontalPanning;

    [ObservableProperty] private int _currentModeBitsPerPixel;
    
    [ObservableProperty] private bool _currentModeIsPlanar;
    
    [ObservableProperty] private int _currentModeLineCompare;
    
    [ObservableProperty] private int _currentModeMouseWidth;
    
    [ObservableProperty] private int _currentModeOriginalHeight;
    
    [ObservableProperty] private int _currentModePixelHeight;
    
    [ObservableProperty] private int _currentModeStartOffset;
    
    [ObservableProperty] private int _currentModeActiveDisplayPage;
    
    [ObservableProperty] private int _currentModeStartVerticalBlanking;
    
    [ObservableProperty] private byte _currentModeVideoModeType;
    
    [ObservableProperty] private int textConsoleHeight;
    
    [ObservableProperty] private int textConsoleWidth;
    
    [ObservableProperty] private bool textConsoleAnsiEnabled;
    
    [ObservableProperty] private byte textConsoleBackgroundColor;
    
    [ObservableProperty] private string textConsoleCursorPosition = "";
    
    [ObservableProperty] private byte textConsoleForegroundColor;
    
    [ObservableProperty] private byte _defaultPaletteLoading;
    
    [ObservableProperty] private byte _totalVramBytes;
}