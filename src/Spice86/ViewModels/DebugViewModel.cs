namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Models.Debugging;

public partial class DebugViewModel : ViewModelBase {
    [ObservableProperty]
    private MachineInfo _machine = new();
    
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();

    [ObservableProperty]
    private DateTime? _lastUpdate = null;

    readonly IVideoState? _videoState;
    readonly IVgaRenderer? _renderer;
    private readonly IPauseStatus? _pauseStatus;

    
    public DebugViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand]
    public void UpdateData() => UpdateValues(this, EventArgs.Empty);

    [ObservableProperty]
    private bool _isPaused;
    
    public DebugViewModel(IUIDispatcherTimer uiDispatcherTimer, IPauseStatus pauseStatus, IVideoState videoState, IVgaRenderer vgaRenderer) {
        _videoState = videoState;
        _renderer = vgaRenderer;
        _pauseStatus = pauseStatus;
        IsPaused = _pauseStatus.IsPaused;
        uiDispatcherTimer.StartNew(TimeSpan.FromMilliseconds(10), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        if(_pauseStatus?.IsPaused is false or null || _renderer is null || _videoState is null) {
            IsPaused = false;
            return;
        }

        IsPaused = true;

        VideoCard.GeneralMiscellaneousOutputRegister = _videoState.GeneralRegisters.MiscellaneousOutput.Value;
        VideoCard.GeneralClockSelect = _videoState.GeneralRegisters.MiscellaneousOutput.ClockSelect;
        VideoCard.GeneralEnableRam = _videoState.GeneralRegisters.MiscellaneousOutput.EnableRam;
        VideoCard.GeneralVerticalSize = _videoState.GeneralRegisters.MiscellaneousOutput.VerticalSize;
        VideoCard.GeneralHorizontalSyncPolarity = _videoState.GeneralRegisters.MiscellaneousOutput.HorizontalSyncPolarity;
        VideoCard.GeneralVerticalSyncPolarity = _videoState.GeneralRegisters.MiscellaneousOutput.VerticalSyncPolarity;
        VideoCard.GeneralIoAddressSelect = _videoState.GeneralRegisters.MiscellaneousOutput.IoAddressSelect;
        VideoCard.GeneralOddPageSelect = _videoState.GeneralRegisters.MiscellaneousOutput.EvenPageSelect;
        VideoCard.GeneralInputStatusRegister0 = _videoState.GeneralRegisters.InputStatusRegister0.Value;
        VideoCard.GeneralCrtInterrupt = _videoState.GeneralRegisters.InputStatusRegister0.CrtInterrupt;
        VideoCard.GeneralSwitchSense = _videoState.GeneralRegisters.InputStatusRegister0.SwitchSense;
        VideoCard.GeneralInputStatusRegister1 = _videoState.GeneralRegisters.InputStatusRegister1.Value;
        VideoCard.GeneralDisplayDisabled = _videoState.GeneralRegisters.InputStatusRegister1.DisplayDisabled;
        VideoCard.GeneralVerticalRetrace = _videoState.GeneralRegisters.InputStatusRegister1.VerticalRetrace;

        VideoCard.DacReadIndex = _videoState.DacRegisters.IndexRegisterReadMode;
        VideoCard.DacWriteIndex = _videoState.DacRegisters.IndexRegisterWriteMode;
        VideoCard.DacPixelMask = _videoState.DacRegisters.PixelMask;
        VideoCard.DacData = _videoState.DacRegisters.DataPeek;

        VideoCard.AttributeControllerColorSelect = _videoState.AttributeControllerRegisters.ColorSelectRegister.Value;
        VideoCard.AttributeControllerOverscanColor = _videoState.AttributeControllerRegisters.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.Value;
        VideoCard.AttributeControllerVideoOutput45Select = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.VideoOutput45Select;
        VideoCard.AttributeControllerPixelWidth8 = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8;
        VideoCard.AttributeControllerPixelPanningCompatibility = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelPanningCompatibility;
        VideoCard.AttributeControllerBlinkingEnabled = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled;
        VideoCard.AttributeControllerLineGraphicsEnabled = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.LineGraphicsEnabled;
        VideoCard.AttributeControllerMonochromeEmulation = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.MonochromeEmulation;
        VideoCard.AttributeControllerGraphicsMode = _videoState.AttributeControllerRegisters.AttributeControllerModeRegister.GraphicsMode;
        VideoCard.AttributeControllerColorPlaneEnable = _videoState.AttributeControllerRegisters.ColorPlaneEnableRegister.Value;
        VideoCard.AttributeControllerHorizontalPixelPanning = _videoState.AttributeControllerRegisters.HorizontalPixelPanning;

        VideoCard.CrtControllerAddressWrap = _videoState.CrtControllerRegisters.CrtModeControlRegister.AddressWrap;
        VideoCard.CrtControllerBytePanning = _videoState.CrtControllerRegisters.PresetRowScanRegister.BytePanning;
        VideoCard.CrtControllerByteWordMode = _videoState.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode;
        VideoCard.CrtControllerCharacterCellHeightRegister = _videoState.CrtControllerRegisters.MaximumScanlineRegister.Value;
        VideoCard.CrtControllerCharacterCellHeight = _videoState.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;
        VideoCard.CrtControllerCompatibilityModeSupport = _videoState.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport;
        VideoCard.CrtControllerCompatibleRead = _videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.CompatibleRead;
        VideoCard.CrtControllerCountByFour = _videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour;
        VideoCard.CrtControllerCountByTwo = _videoState.CrtControllerRegisters.CrtModeControlRegister.CountByTwo;
        VideoCard.CrtControllerCrtcScanDouble = _videoState.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble;
        VideoCard.CrtControllerCrtModeControl = _videoState.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerCursorEnd = _videoState.CrtControllerRegisters.TextCursorEndRegister.Value;
        VideoCard.CrtControllerCursorLocationHigh = _videoState.CrtControllerRegisters.TextCursorLocationHigh;
        VideoCard.CrtControllerCursorLocationLow = _videoState.CrtControllerRegisters.TextCursorLocationLow;
        VideoCard.CrtControllerCursorStart = _videoState.CrtControllerRegisters.TextCursorStartRegister.Value;
        VideoCard.CrtControllerDisableTextCursor = _videoState.CrtControllerRegisters.TextCursorStartRegister.DisableTextCursor;
        VideoCard.CrtControllerDisableVerticalInterrupt = _videoState.CrtControllerRegisters.VerticalSyncEndRegister.DisableVerticalInterrupt;
        VideoCard.CrtControllerDisplayEnableSkew = _videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;
        VideoCard.CrtControllerDoubleWordMode = _videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode;
        VideoCard.CrtControllerEndHorizontalBlanking = _videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.Value;
        VideoCard.CrtControllerEndHorizontalDisplay = _videoState.CrtControllerRegisters.HorizontalDisplayEnd;
        VideoCard.CrtControllerEndHorizontalRetrace = _videoState.CrtControllerRegisters.HorizontalSyncEndRegister.Value;
        VideoCard.CrtControllerEndVerticalBlanking = _videoState.CrtControllerRegisters.VerticalBlankingEnd;
        VideoCard.CrtControllerHorizontalBlankingEnd = _videoState.CrtControllerRegisters.HorizontalBlankingEndValue;
        VideoCard.CrtControllerHorizontalSyncDelay = _videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncDelay;
        VideoCard.CrtControllerHorizontalSyncEnd = _videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd;
        VideoCard.CrtControllerHorizontalTotal = _videoState.CrtControllerRegisters.HorizontalTotal;
        VideoCard.CrtControllerLineCompareRegister = _videoState.CrtControllerRegisters.LineCompare;
        VideoCard.CrtControllerLineCompare = _videoState.CrtControllerRegisters.LineCompareValue;
        VideoCard.CrtControllerOffset = _videoState.CrtControllerRegisters.Offset;
        VideoCard.CrtControllerOverflow = _videoState.CrtControllerRegisters.OverflowRegister.Value;
        VideoCard.CrtControllerPresetRowScan = _videoState.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
        VideoCard.CrtControllerPresetRowScanRegister = _videoState.CrtControllerRegisters.PresetRowScanRegister.Value;
        VideoCard.CrtControllerRefreshCyclesPerScanline = _videoState.CrtControllerRegisters.VerticalSyncEndRegister.RefreshCyclesPerScanline;
        VideoCard.CrtControllerSelectRowScanCounter = _videoState.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter;
        VideoCard.CrtControllerStartAddress = _videoState.CrtControllerRegisters.ScreenStartAddress;
        VideoCard.CrtControllerStartAddressHigh = _videoState.CrtControllerRegisters.ScreenStartAddressHigh;
        VideoCard.CrtControllerStartAddressLow = _videoState.CrtControllerRegisters.ScreenStartAddressLow;
        VideoCard.CrtControllerStartHorizontalBlanking = _videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerStartHorizontalRetrace = _videoState.CrtControllerRegisters.HorizontalSyncStart;
        VideoCard.CrtControllerStartVerticalBlanking = _videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerTextCursorEnd = _videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorEnd;
        VideoCard.CrtControllerTextCursorLocation = _videoState.CrtControllerRegisters.TextCursorLocation;
        VideoCard.CrtControllerTextCursorSkew = _videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorSkew;
        VideoCard.CrtControllerTextCursorStart = _videoState.CrtControllerRegisters.TextCursorStartRegister.TextCursorStart;
        VideoCard.CrtControllerTimingEnable = _videoState.CrtControllerRegisters.CrtModeControlRegister.TimingEnable;
        VideoCard.CrtControllerUnderlineLocation = _videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.Value;
        VideoCard.CrtControllerUnderlineScanline = _videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.UnderlineScanline;
        VideoCard.CrtControllerVerticalBlankingStart = _videoState.CrtControllerRegisters.VerticalBlankingStartValue;
        VideoCard.CrtControllerVerticalDisplayEnd = _videoState.CrtControllerRegisters.VerticalDisplayEndValue;
        VideoCard.CrtControllerVerticalDisplayEndRegister = _videoState.CrtControllerRegisters.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = _videoState.CrtControllerRegisters.VerticalSyncEndRegister.Value;
        VideoCard.CrtControllerVerticalRetraceStart = _videoState.CrtControllerRegisters.VerticalSyncStart;
        VideoCard.CrtControllerVerticalSyncStart = _videoState.CrtControllerRegisters.VerticalSyncStartValue;
        VideoCard.CrtControllerVerticalTimingHalved = _videoState.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved;
        VideoCard.CrtControllerVerticalTotal = _videoState.CrtControllerRegisters.VerticalTotalValue;
        VideoCard.CrtControllerVerticalTotalRegister = _videoState.CrtControllerRegisters.VerticalTotal;
        VideoCard.CrtControllerWriteProtect = _videoState.CrtControllerRegisters.VerticalSyncEndRegister.WriteProtect;
        
        VideoCard.GraphicsDataRotate = _videoState.GraphicsControllerRegisters.DataRotateRegister.Value;
        VideoCard.GraphicsRotateCount = _videoState.GraphicsControllerRegisters.DataRotateRegister.RotateCount;
        VideoCard.GraphicsFunctionSelect = _videoState.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect;
        VideoCard.GraphicsBitMask = _videoState.GraphicsControllerRegisters.BitMask;
        VideoCard.GraphicsColorCompare = _videoState.GraphicsControllerRegisters.ColorCompare;
        VideoCard.GraphicsReadMode = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode;
        VideoCard.GraphicsWriteMode = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode;
        VideoCard.GraphicsOddEven = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.OddEven;
        VideoCard.GraphicsShiftRegisterMode = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode;
        VideoCard.GraphicsIn256ColorMode = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode;
        VideoCard.GraphicsModeRegister = _videoState.GraphicsControllerRegisters.GraphicsModeRegister.Value;
        VideoCard.GraphicsMiscellaneousGraphics = _videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.Value;
        VideoCard.GraphicsGraphicsMode = _videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode;
        VideoCard.GraphicsChainOddMapsToEven = _videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven;
        VideoCard.GraphicsMemoryMap = _videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.MemoryMap;
        VideoCard.GraphicsReadMapSelect = _videoState.GraphicsControllerRegisters.ReadMapSelectRegister.PlaneSelect;
        VideoCard.GraphicsSetReset = _videoState.GraphicsControllerRegisters.SetReset.Value;
        VideoCard.GraphicsColorDontCare = _videoState.GraphicsControllerRegisters.ColorDontCare;
        VideoCard.GraphicsEnableSetReset = _videoState.GraphicsControllerRegisters.EnableSetReset.Value;

        VideoCard.SequencerResetRegister = _videoState.SequencerRegisters.ResetRegister.Value;
        VideoCard.SequencerSynchronousReset = _videoState.SequencerRegisters.ResetRegister.SynchronousReset;
        VideoCard.SequencerAsynchronousReset = _videoState.SequencerRegisters.ResetRegister.AsynchronousReset;
        VideoCard.SequencerClockingModeRegister = _videoState.SequencerRegisters.ClockingModeRegister.Value;
        VideoCard.SequencerDotsPerClock = _videoState.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        VideoCard.SequencerShiftLoad = _videoState.SequencerRegisters.ClockingModeRegister.ShiftLoad;
        VideoCard.SequencerDotClock = _videoState.SequencerRegisters.ClockingModeRegister.HalfDotClock;
        VideoCard.SequencerShift4 = _videoState.SequencerRegisters.ClockingModeRegister.Shift4;
        VideoCard.SequencerScreenOff = _videoState.SequencerRegisters.ClockingModeRegister.ScreenOff;
        VideoCard.SequencerPlaneMask = _videoState.SequencerRegisters.PlaneMaskRegister.Value;
        VideoCard.SequencerCharacterMapSelect = _videoState.SequencerRegisters.CharacterMapSelectRegister.Value;
        VideoCard.SequencerCharacterMapA = _videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapA;
        VideoCard.SequencerCharacterMapB = _videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapB;
        VideoCard.SequencerSequencerMemoryMode = _videoState.SequencerRegisters.MemoryModeRegister.Value;
        VideoCard.SequencerExtendedMemory = _videoState.SequencerRegisters.MemoryModeRegister.ExtendedMemory;
        VideoCard.SequencerOddEvenMode = _videoState.SequencerRegisters.MemoryModeRegister.OddEvenMode;
        VideoCard.SequencerChain4Mode = _videoState.SequencerRegisters.MemoryModeRegister.Chain4Mode;

        VideoCard.RendererWidth = _renderer.Width;
        VideoCard.RendererHeight = _renderer.Height;
        VideoCard.RendererBufferSize = _renderer.BufferSize;
        VideoCard.LastFrameRenderTime = _renderer.LastFrameRenderTime;

        LastUpdate = DateTime.Now;
    }
}