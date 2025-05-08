namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers.General;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

public partial class VideoCardInfo : ObservableObject {
    [ObservableProperty]
    private byte _generalMiscellaneousOutputRegister;

    [ObservableProperty]
    private MiscellaneousOutput.ClockSelectValue _generalClockSelect;

    [ObservableProperty]
    private bool _generalEnableRam;

    [ObservableProperty]
    private int _generalVerticalSize;

    [ObservableProperty]
    private MiscellaneousOutput.PolarityValue _generalHorizontalSyncPolarity;

    [ObservableProperty]
    private MiscellaneousOutput.PolarityValue _generalVerticalSyncPolarity;

    [ObservableProperty]
    private MiscellaneousOutput.IoAddressSelectValue _generalIoAddressSelect;

    [ObservableProperty]
    private bool _generalOddPageSelect;

    [ObservableProperty]
    private byte _generalInputStatusRegister0;

    [ObservableProperty]
    private bool _generalCrtInterrupt;

    [ObservableProperty]
    private bool _generalSwitchSense;

    [ObservableProperty]
    private byte _generalInputStatusRegister1;

    [ObservableProperty]
    private bool _generalDisplayDisabled;

    [ObservableProperty]
    private bool _generalVerticalRetrace;

    [ObservableProperty]
    private byte _dacReadIndex;

    [ObservableProperty]
    private byte _dacWriteIndex;

    [ObservableProperty]
    private byte _dacPixelMask;

    [ObservableProperty]
    private byte _dacData;

    [ObservableProperty]
    private byte _graphicsDataRotate;

    [ObservableProperty]
    private byte _graphicsRotateCount;

    [ObservableProperty]
    private FunctionSelect _graphicsFunctionSelect;

    [ObservableProperty]
    private byte _graphicsBitMask;

    [ObservableProperty]
    private byte _graphicsColorCompare;

    [ObservableProperty]
    private ReadMode _graphicsReadMode;

    [ObservableProperty]
    private WriteMode _graphicsWriteMode;

    [ObservableProperty]
    private bool _graphicsOddEven;

    [ObservableProperty]
    private ShiftRegisterMode _graphicsShiftRegisterMode;

    [ObservableProperty]
    private bool _graphicsIn256ColorMode;

    [ObservableProperty]
    private byte _graphicsMiscellaneousGraphics;

    [ObservableProperty]
    private byte _graphicsModeRegister;

    [ObservableProperty]
    private bool _graphicsGraphicsMode;

    [ObservableProperty]
    private bool _graphicsChainOddMapsToEven;

    [ObservableProperty]
    private byte _graphicsMemoryMap;

    [ObservableProperty]
    private uint _graphicsSetReset;

    [ObservableProperty]
    private uint _graphicsColorDontCare;

    [ObservableProperty]
    private uint _graphicsEnableSetReset;

    [ObservableProperty]
    private byte _graphicsReadMapSelect;

    [ObservableProperty]
    private byte _sequencerResetRegister;

    [ObservableProperty]
    private bool _sequencerSynchronousReset;

    [ObservableProperty]
    private bool _sequencerAsynchronousReset;

    [ObservableProperty]
    private byte _sequencerClockingModeRegister;

    [ObservableProperty]
    private int _sequencerDotsPerClock;

    [ObservableProperty]
    private bool _sequencerShiftLoad;

    [ObservableProperty]
    private bool _sequencerDotClock;

    [ObservableProperty]
    private bool _sequencerShift4;

    [ObservableProperty]
    private bool _sequencerScreenOff;

    [ObservableProperty]
    private byte _sequencerPlaneMask;

    [ObservableProperty]
    private byte _sequencerCharacterMapSelect;

    [ObservableProperty]
    private int _sequencerCharacterMapA;

    [ObservableProperty]
    private int _sequencerCharacterMapB;

    [ObservableProperty]
    private byte _sequencerSequencerMemoryMode;

    [ObservableProperty]
    private bool _sequencerExtendedMemory;

    [ObservableProperty]
    private bool _sequencerOddEvenMode;

    [ObservableProperty]
    private bool _sequencerChain4Mode;

    [ObservableProperty]
    private byte _attributeControllerColorSelect;

    [ObservableProperty]
    private byte _attributeControllerOverscanColor;

    [ObservableProperty]
    private byte _attributeControllerAttributeModeControl;

    [ObservableProperty]
    private bool _attributeControllerVideoOutput45Select;

    [ObservableProperty]
    private bool _attributeControllerPixelWidth8;

    [ObservableProperty]
    private bool _attributeControllerPixelPanningCompatibility;

    [ObservableProperty]
    private bool _attributeControllerBlinkingEnabled;

    [ObservableProperty]
    private bool _attributeControllerLineGraphicsEnabled;

    [ObservableProperty]
    private bool _attributeControllerMonochromeEmulation;

    [ObservableProperty]
    private bool _attributeControllerGraphicsMode;

    [ObservableProperty]
    private byte _attributeControllerColorPlaneEnable;

    [ObservableProperty]
    private byte _attributeControllerHorizontalPixelPanning;

    [ObservableProperty]
    private bool _crtControllerAddressWrap;

    [ObservableProperty]
    private bool _crtControllerClearVerticalInterrupt;

    [ObservableProperty]
    private bool _crtControllerCompatibilityModeSupport;

    [ObservableProperty]
    private bool _crtControllerCompatibleRead;

    [ObservableProperty]
    private bool _crtControllerCountByFour;

    [ObservableProperty]
    private bool _crtControllerCountByTwo;

    [ObservableProperty]
    private bool _crtControllerCrtcScanDouble;

    [ObservableProperty]
    private bool _crtControllerDisableTextCursor;

    [ObservableProperty]
    private bool _crtControllerDisableVerticalInterrupt;

    [ObservableProperty]
    private bool _crtControllerDoubleWordMode;

    [ObservableProperty]
    private bool _crtControllerSelectRowScanCounter;

    [ObservableProperty]
    private bool _crtControllerTimingEnable;

    [ObservableProperty]
    private bool _crtControllerVerticalTimingHalved;

    [ObservableProperty]
    private bool _crtControllerWriteProtect;

    [ObservableProperty]
    private byte _crtControllerCrtModeControl;

    [ObservableProperty]
    private byte _crtControllerCursorEnd;

    [ObservableProperty]
    private byte _crtControllerCursorLocationHigh;

    [ObservableProperty]
    private byte _crtControllerCursorLocationLow;

    [ObservableProperty]
    private byte _crtControllerCursorStart;

    [ObservableProperty]
    private byte _crtControllerEndHorizontalBlanking;

    [ObservableProperty]
    private byte _crtControllerEndHorizontalDisplay;

    [ObservableProperty]
    private byte _crtControllerEndHorizontalRetrace;

    [ObservableProperty]
    private byte _crtControllerEndVerticalBlanking;

    [ObservableProperty]
    private byte _crtControllerHorizontalTotal;

    [ObservableProperty]
    private byte _crtControllerLineCompareRegister;

    [ObservableProperty]
    private byte _crtControllerCharacterCellHeightRegister;

    [ObservableProperty]
    private byte _crtControllerOffset;

    [ObservableProperty]
    private byte _crtControllerOverflow;

    [ObservableProperty]
    private byte _crtControllerPresetRowScanRegister;

    [ObservableProperty]
    private byte _crtControllerStartAddressHigh;

    [ObservableProperty]
    private byte _crtControllerStartAddressLow;

    [ObservableProperty]
    private byte _crtControllerStartHorizontalBlanking;

    [ObservableProperty]
    private byte _crtControllerStartHorizontalRetrace;

    [ObservableProperty]
    private byte _crtControllerStartVerticalBlanking;

    [ObservableProperty]
    private byte _crtControllerUnderlineLocation;

    [ObservableProperty]
    private byte _crtControllerVerticalDisplayEndRegister;

    [ObservableProperty]
    private byte _crtControllerVerticalRetraceEnd;

    [ObservableProperty]
    private byte _crtControllerVerticalRetraceStart;

    [ObservableProperty]
    private byte _crtControllerVerticalTotalRegister;

    [ObservableProperty]
    private ByteWordMode _crtControllerByteWordMode;

    [ObservableProperty]
    private int _crtControllerBytePanning;

    [ObservableProperty]
    private int _crtControllerCharacterCellHeight;

    [ObservableProperty]
    private int _crtControllerDisplayEnableSkew;

    [ObservableProperty]
    private int _crtControllerHorizontalBlankingEnd;

    [ObservableProperty]
    private int _crtControllerHorizontalSyncDelay;

    [ObservableProperty]
    private int _crtControllerHorizontalSyncEnd;

    [ObservableProperty]
    private int _crtControllerLineCompare;

    [ObservableProperty]
    private int _crtControllerPresetRowScan;

    [ObservableProperty]
    private int _crtControllerRefreshCyclesPerScanline;

    [ObservableProperty]
    private int _crtControllerStartAddress;

    [ObservableProperty]
    private int _crtControllerTextCursorEnd;

    [ObservableProperty]
    private int _crtControllerTextCursorLocation;

    [ObservableProperty]
    private int _crtControllerTextCursorSkew;

    [ObservableProperty]
    private int _crtControllerTextCursorStart;

    [ObservableProperty]
    private int _crtControllerUnderlineScanline;

    [ObservableProperty]
    private int _crtControllerVerticalBlankingStart;

    [ObservableProperty]
    private int _crtControllerVerticalDisplayEnd;

    [ObservableProperty]
    private int _crtControllerVerticalSyncStart;

    [ObservableProperty]
    private int _crtControllerVerticalTotal;

    [ObservableProperty]
    private int _rendererWidth;

    [ObservableProperty]
    private int _rendererHeight;

    [ObservableProperty]
    private int _rendererBufferSize;

    [ObservableProperty]
    private TimeSpan _lastFrameRenderTime;
}