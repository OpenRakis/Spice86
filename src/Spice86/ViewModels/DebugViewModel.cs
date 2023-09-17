namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Models.Debugging;

public partial class DebugViewModel : ViewModelBase, IEmulatorDebugger {
    [ObservableProperty]
    private MachineInfo _machine = new();
    
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();

    [ObservableProperty]
    private DateTime? _lastUpdate = null;

    private readonly IPauseStatus? _pauseStatus;

    [ObservableProperty] private bool _isLoading = true;
    
    [ObservableProperty]
    private IMemory? _memory;
    
    public DebugViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand]
    public void UpdateData() => UpdateValues(this, EventArgs.Empty);

    [ObservableProperty]
    private bool _isPaused;

    private readonly IDebuggableComponent? _programExecutor;

    private readonly IUIDispatcherTimer? _uiDispatcherTimer;
    
    public DebugViewModel(IUIDispatcherTimer uiDispatcherTimer, IDebuggableComponent programExecutor, IPauseStatus pauseStatus) {
        _programExecutor = programExecutor;
        _pauseStatus = pauseStatus;
        _uiDispatcherTimer = uiDispatcherTimer;
        IsPaused = _pauseStatus.IsPaused;
    }

    [RelayCommand]
    public void StartObserverTimer() {
        _uiDispatcherTimer?.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        IsPaused = true;
        _programExecutor?.Accept(this);
        LastUpdate = DateTime.Now;
        IsLoading = false;
    }

    [ObservableProperty]
    private StateInfo _state = new();

    public void VisitMainMemory(IMemory memory) {
        Memory = memory;
    }

    public void VisitCpuState(State state) {
        State.AH = state.AH;
        State.AL = state.AL;
        State.AX = state.AX;
        State.EAX = state.EAX;
        State.BH = state.BH;
        State.BL = state.BL;
        State.BX = state.BX;
        State.EBX = state.EBX;
        State.CH =state.CH;
        State.CL = state.CL;
        State.CX = state.CX;
        State.ECX = state.ECX;
        State.DH = state.DH;
        State.DL = state.DL;
        State.DX = state.DX;
        State.EDX = state.EDX;
        State.DI = state.DI;
        State.EDI = state.EDI;
        State.SI = state.SI;
        State.ES = state.ES;
        State.BP = state.BP;
        State.EBP = state.EBP;
        State.SP = state.SP;
        State.ESP = state.ESP;
        State.CS = state.CS;
        State.DS = state.DS;
        State.ES = state.ES;
        State.FS = state.FS;
        State.GS = state.GS;
        State.SS = state.SS;
        State.IP = state.IP;
        State.Cycles = state.Cycles;
        State.Direction8 = state.Direction8;
        State.Direction16 = state.Direction16;
        State.Direction32 = state.Direction32;
        State.AuxiliaryFlag = state.AuxiliaryFlag;
        State.CarryFlag = state.CarryFlag;
        State.DirectionFlag = state.DirectionFlag;
        State.InterruptFlag = state.InterruptFlag;
        State.OverflowFlag = state.OverflowFlag;
        State.ParityFlag = state.ParityFlag;
        State.ZeroFlag = state.ZeroFlag;
        State.ContinueZeroFlag = state.ContinueZeroFlagValue;
        State.StackPhysicalAddress = state.StackPhysicalAddress;
        State.SegmentOverrideIndex = state.SegmentOverrideIndex;
        State.IpPhysicalAddress = state.IpPhysicalAddress;
        State.IsRunning = state.IsRunning;
    }

    public void VisitVgaRenderer(IVgaRenderer vgaRenderer) {
        VideoCard.RendererWidth = vgaRenderer.Width;
        VideoCard.RendererHeight = vgaRenderer.Height;
        VideoCard.RendererBufferSize = vgaRenderer.BufferSize;
        VideoCard.LastFrameRenderTime = vgaRenderer.LastFrameRenderTime;
    }

    public void VisitVideoState(IVideoState videoState) {
        VideoCard.GeneralMiscellaneousOutputRegister = videoState.GeneralRegisters.MiscellaneousOutput.Value;
        VideoCard.GeneralClockSelect = videoState.GeneralRegisters.MiscellaneousOutput.ClockSelect;
        VideoCard.GeneralEnableRam = videoState.GeneralRegisters.MiscellaneousOutput.EnableRam;
        VideoCard.GeneralVerticalSize = videoState.GeneralRegisters.MiscellaneousOutput.VerticalSize;
        VideoCard.GeneralHorizontalSyncPolarity = videoState.GeneralRegisters.MiscellaneousOutput.HorizontalSyncPolarity;
        VideoCard.GeneralVerticalSyncPolarity = videoState.GeneralRegisters.MiscellaneousOutput.VerticalSyncPolarity;
        VideoCard.GeneralIoAddressSelect = videoState.GeneralRegisters.MiscellaneousOutput.IoAddressSelect;
        VideoCard.GeneralOddPageSelect = videoState.GeneralRegisters.MiscellaneousOutput.EvenPageSelect;
        VideoCard.GeneralInputStatusRegister0 = videoState.GeneralRegisters.InputStatusRegister0.Value;
        VideoCard.GeneralCrtInterrupt = videoState.GeneralRegisters.InputStatusRegister0.CrtInterrupt;
        VideoCard.GeneralSwitchSense = videoState.GeneralRegisters.InputStatusRegister0.SwitchSense;
        VideoCard.GeneralInputStatusRegister1 = videoState.GeneralRegisters.InputStatusRegister1.Value;
        VideoCard.GeneralDisplayDisabled = videoState.GeneralRegisters.InputStatusRegister1.DisplayDisabled;
        VideoCard.GeneralVerticalRetrace = videoState.GeneralRegisters.InputStatusRegister1.VerticalRetrace;

        VideoCard.DacReadIndex = videoState.DacRegisters.IndexRegisterReadMode;
        VideoCard.DacWriteIndex = videoState.DacRegisters.IndexRegisterWriteMode;
        VideoCard.DacPixelMask = videoState.DacRegisters.PixelMask;
        VideoCard.DacData = videoState.DacRegisters.DataPeek;

        VideoCard.AttributeControllerColorSelect = videoState.AttributeControllerRegisters.ColorSelectRegister.Value;
        VideoCard.AttributeControllerOverscanColor = videoState.AttributeControllerRegisters.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.Value;
        VideoCard.AttributeControllerVideoOutput45Select = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.VideoOutput45Select;
        VideoCard.AttributeControllerPixelWidth8 = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8;
        VideoCard.AttributeControllerPixelPanningCompatibility = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelPanningCompatibility;
        VideoCard.AttributeControllerBlinkingEnabled = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled;
        VideoCard.AttributeControllerLineGraphicsEnabled = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.LineGraphicsEnabled;
        VideoCard.AttributeControllerMonochromeEmulation = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.MonochromeEmulation;
        VideoCard.AttributeControllerGraphicsMode = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.GraphicsMode;
        VideoCard.AttributeControllerColorPlaneEnable = videoState.AttributeControllerRegisters.ColorPlaneEnableRegister.Value;
        VideoCard.AttributeControllerHorizontalPixelPanning = videoState.AttributeControllerRegisters.HorizontalPixelPanning;

        VideoCard.CrtControllerAddressWrap = videoState.CrtControllerRegisters.CrtModeControlRegister.AddressWrap;
        VideoCard.CrtControllerBytePanning = videoState.CrtControllerRegisters.PresetRowScanRegister.BytePanning;
        VideoCard.CrtControllerByteWordMode = videoState.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode;
        VideoCard.CrtControllerCharacterCellHeightRegister = videoState.CrtControllerRegisters.MaximumScanlineRegister.Value;
        VideoCard.CrtControllerCharacterCellHeight = videoState.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;
        VideoCard.CrtControllerCompatibilityModeSupport = videoState.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport;
        VideoCard.CrtControllerCompatibleRead = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.CompatibleRead;
        VideoCard.CrtControllerCountByFour = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour;
        VideoCard.CrtControllerCountByTwo = videoState.CrtControllerRegisters.CrtModeControlRegister.CountByTwo;
        VideoCard.CrtControllerCrtcScanDouble = videoState.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble;
        VideoCard.CrtControllerCrtModeControl = videoState.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerCursorEnd = videoState.CrtControllerRegisters.TextCursorEndRegister.Value;
        VideoCard.CrtControllerCursorLocationHigh = videoState.CrtControllerRegisters.TextCursorLocationHigh;
        VideoCard.CrtControllerCursorLocationLow = videoState.CrtControllerRegisters.TextCursorLocationLow;
        VideoCard.CrtControllerCursorStart = videoState.CrtControllerRegisters.TextCursorStartRegister.Value;
        VideoCard.CrtControllerDisableTextCursor = videoState.CrtControllerRegisters.TextCursorStartRegister.DisableTextCursor;
        VideoCard.CrtControllerDisableVerticalInterrupt = videoState.CrtControllerRegisters.VerticalSyncEndRegister.DisableVerticalInterrupt;
        VideoCard.CrtControllerDisplayEnableSkew = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;
        VideoCard.CrtControllerDoubleWordMode = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode;
        VideoCard.CrtControllerEndHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.Value;
        VideoCard.CrtControllerEndHorizontalDisplay = videoState.CrtControllerRegisters.HorizontalDisplayEnd;
        VideoCard.CrtControllerEndHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.Value;
        VideoCard.CrtControllerEndVerticalBlanking = videoState.CrtControllerRegisters.VerticalBlankingEnd;
        VideoCard.CrtControllerHorizontalBlankingEnd = videoState.CrtControllerRegisters.HorizontalBlankingEndValue;
        VideoCard.CrtControllerHorizontalSyncDelay = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncDelay;
        VideoCard.CrtControllerHorizontalSyncEnd = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd;
        VideoCard.CrtControllerHorizontalTotal = videoState.CrtControllerRegisters.HorizontalTotal;
        VideoCard.CrtControllerLineCompareRegister = videoState.CrtControllerRegisters.LineCompare;
        VideoCard.CrtControllerLineCompare = videoState.CrtControllerRegisters.LineCompareValue;
        VideoCard.CrtControllerOffset = videoState.CrtControllerRegisters.Offset;
        VideoCard.CrtControllerOverflow = videoState.CrtControllerRegisters.OverflowRegister.Value;
        VideoCard.CrtControllerPresetRowScan = videoState.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
        VideoCard.CrtControllerPresetRowScanRegister = videoState.CrtControllerRegisters.PresetRowScanRegister.Value;
        VideoCard.CrtControllerRefreshCyclesPerScanline = videoState.CrtControllerRegisters.VerticalSyncEndRegister.RefreshCyclesPerScanline;
        VideoCard.CrtControllerSelectRowScanCounter = videoState.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter;
        VideoCard.CrtControllerStartAddress = videoState.CrtControllerRegisters.ScreenStartAddress;
        VideoCard.CrtControllerStartAddressHigh = videoState.CrtControllerRegisters.ScreenStartAddressHigh;
        VideoCard.CrtControllerStartAddressLow = videoState.CrtControllerRegisters.ScreenStartAddressLow;
        VideoCard.CrtControllerStartHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerStartHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncStart;
        VideoCard.CrtControllerStartVerticalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerTextCursorEnd = videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorEnd;
        VideoCard.CrtControllerTextCursorLocation = videoState.CrtControllerRegisters.TextCursorLocation;
        VideoCard.CrtControllerTextCursorSkew = videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorSkew;
        VideoCard.CrtControllerTextCursorStart = videoState.CrtControllerRegisters.TextCursorStartRegister.TextCursorStart;
        VideoCard.CrtControllerTimingEnable = videoState.CrtControllerRegisters.CrtModeControlRegister.TimingEnable;
        VideoCard.CrtControllerUnderlineLocation = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.Value;
        VideoCard.CrtControllerUnderlineScanline = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.UnderlineScanline;
        VideoCard.CrtControllerVerticalBlankingStart = videoState.CrtControllerRegisters.VerticalBlankingStartValue;
        VideoCard.CrtControllerVerticalDisplayEnd = videoState.CrtControllerRegisters.VerticalDisplayEndValue;
        VideoCard.CrtControllerVerticalDisplayEndRegister = videoState.CrtControllerRegisters.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = videoState.CrtControllerRegisters.VerticalSyncEndRegister.Value;
        VideoCard.CrtControllerVerticalRetraceStart = videoState.CrtControllerRegisters.VerticalSyncStart;
        VideoCard.CrtControllerVerticalSyncStart = videoState.CrtControllerRegisters.VerticalSyncStartValue;
        VideoCard.CrtControllerVerticalTimingHalved = videoState.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved;
        VideoCard.CrtControllerVerticalTotal = videoState.CrtControllerRegisters.VerticalTotalValue;
        VideoCard.CrtControllerVerticalTotalRegister = videoState.CrtControllerRegisters.VerticalTotal;
        VideoCard.CrtControllerWriteProtect = videoState.CrtControllerRegisters.VerticalSyncEndRegister.WriteProtect;
        
        VideoCard.GraphicsDataRotate = videoState.GraphicsControllerRegisters.DataRotateRegister.Value;
        VideoCard.GraphicsRotateCount = videoState.GraphicsControllerRegisters.DataRotateRegister.RotateCount;
        VideoCard.GraphicsFunctionSelect = videoState.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect;
        VideoCard.GraphicsBitMask = videoState.GraphicsControllerRegisters.BitMask;
        VideoCard.GraphicsColorCompare = videoState.GraphicsControllerRegisters.ColorCompare;
        VideoCard.GraphicsReadMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode;
        VideoCard.GraphicsWriteMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode;
        VideoCard.GraphicsOddEven = videoState.GraphicsControllerRegisters.GraphicsModeRegister.OddEven;
        VideoCard.GraphicsShiftRegisterMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode;
        VideoCard.GraphicsIn256ColorMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode;
        VideoCard.GraphicsModeRegister = videoState.GraphicsControllerRegisters.GraphicsModeRegister.Value;
        VideoCard.GraphicsMiscellaneousGraphics = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.Value;
        VideoCard.GraphicsGraphicsMode = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode;
        VideoCard.GraphicsChainOddMapsToEven = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven;
        VideoCard.GraphicsMemoryMap = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.MemoryMap;
        VideoCard.GraphicsReadMapSelect = videoState.GraphicsControllerRegisters.ReadMapSelectRegister.PlaneSelect;
        VideoCard.GraphicsSetReset = videoState.GraphicsControllerRegisters.SetReset.Value;
        VideoCard.GraphicsColorDontCare = videoState.GraphicsControllerRegisters.ColorDontCare;
        VideoCard.GraphicsEnableSetReset = videoState.GraphicsControllerRegisters.EnableSetReset.Value;

        VideoCard.SequencerResetRegister = videoState.SequencerRegisters.ResetRegister.Value;
        VideoCard.SequencerSynchronousReset = videoState.SequencerRegisters.ResetRegister.SynchronousReset;
        VideoCard.SequencerAsynchronousReset = videoState.SequencerRegisters.ResetRegister.AsynchronousReset;
        VideoCard.SequencerClockingModeRegister = videoState.SequencerRegisters.ClockingModeRegister.Value;
        VideoCard.SequencerDotsPerClock = videoState.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        VideoCard.SequencerShiftLoad = videoState.SequencerRegisters.ClockingModeRegister.ShiftLoad;
        VideoCard.SequencerDotClock = videoState.SequencerRegisters.ClockingModeRegister.HalfDotClock;
        VideoCard.SequencerShift4 = videoState.SequencerRegisters.ClockingModeRegister.Shift4;
        VideoCard.SequencerScreenOff = videoState.SequencerRegisters.ClockingModeRegister.ScreenOff;
        VideoCard.SequencerPlaneMask = videoState.SequencerRegisters.PlaneMaskRegister.Value;
        VideoCard.SequencerCharacterMapSelect = videoState.SequencerRegisters.CharacterMapSelectRegister.Value;
        VideoCard.SequencerCharacterMapA = videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapA;
        VideoCard.SequencerCharacterMapB = videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapB;
        VideoCard.SequencerSequencerMemoryMode = videoState.SequencerRegisters.MemoryModeRegister.Value;
        VideoCard.SequencerExtendedMemory = videoState.SequencerRegisters.MemoryModeRegister.ExtendedMemory;
        VideoCard.SequencerOddEvenMode = videoState.SequencerRegisters.MemoryModeRegister.OddEvenMode;
        VideoCard.SequencerChain4Mode = videoState.SequencerRegisters.MemoryModeRegister.Chain4Mode;
    }

    public void VisitDacPalette(ArgbPalette argbPalette) {
    }

    public void VisitDacRegisters(DacRegisters dacRegisters) {
    }

    public void VisitVgaCard(VgaCard vgaCard) {
    }

    public void VisitCpu(Cpu cpu) {
    }
}