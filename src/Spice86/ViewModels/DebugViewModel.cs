namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Iced.Intel;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;
using Spice86.Wrappers;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;

public partial class DebugViewModel : ViewModelBase, IEmulatorDebugger, IDebugViewModel {
    [ObservableProperty]
    private MachineInfo _machine = new();
    
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();

    [ObservableProperty]
    private DateTime? _lastUpdate;

    private readonly IPauseStatus? _pauseStatus;

    [ObservableProperty] private bool _isLoading = true;
    
    private IMemory? _memory;
    
    public DebugViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand]
    public void Step() {
        _programExecutor?.Step();
        IsLoading = true;
        UpdateData();
    }

    [RelayCommand]
    public void UpdateData() => UpdateValues(this, EventArgs.Empty);

    [ObservableProperty]
    private bool _isPaused;

    private IProgramExecutor? _programExecutor;

    public IProgramExecutor? ProgramExecutor {
        get => _programExecutor;
        set {
            if (value is not null && _uiDispatcherTimer is not null) {
                _programExecutor = value;
                PaletteViewModel = new(_uiDispatcherTimer, value);
            }
        }
    }

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();

    [ObservableProperty]
    private int _selectedTab;

    private readonly IUIDispatcherTimer? _uiDispatcherTimer;

    [ObservableProperty]
    private PaletteViewModel? _paletteViewModel;
    
    public DebugViewModel(IUIDispatcherTimer uiDispatcherTimer, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        IsPaused = _pauseStatus.IsPaused;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
        _uiDispatcherTimer = uiDispatcherTimer;
    }

    private void OnPauseStatusChanged(object? sender, PropertyChangedEventArgs e) {
        IsPaused = _pauseStatus?.IsPaused is true;
        if (IsPaused) {
            _mustRefreshMainMemory = true;
        }
    }

    [RelayCommand]
    public void StartObserverTimer() {
        _uiDispatcherTimer?.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
    }

    [ObservableProperty] private bool _isEditingMemory;

    [ObservableProperty]
    private string? _memoryEditAddress;

    [ObservableProperty]
    private string? _memoryEditValue = "";

    [RelayCommand]
    public void EditMemory() {
        if (_memory is null) {
            return;
        }
        IsEditingMemory = true;
        if (MemoryEditAddress is not null && TryParseMemoryAddress(MemoryEditAddress, out uint? memoryEditAddressValue)) {
            MemoryEditValue = Convert.ToHexString(_memory.GetData(memoryEditAddressValue.Value, (uint)(MemoryEditValue is null ? sizeof(ushort) : MemoryEditValue.Length)));
        }
    }

    private bool TryParseMemoryAddress(string? memoryAddress,  [NotNullWhen(true)] out uint? address) {
        if (string.IsNullOrWhiteSpace(memoryAddress)) {
            address = null;
            return false;
        }

        try {
            if (memoryAddress.Contains(":")) {
                string[] split = memoryAddress.Split(":");
                if (split.Length > 1 &&
                    ushort.TryParse(split[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort segment) &&
                    ushort.TryParse(split[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort offset)) {
                    address = MemoryUtils.ToPhysicalAddress(segment, offset);
                    return true;
                }
            } else if(uint.TryParse(memoryAddress, CultureInfo.InvariantCulture, out uint value)) {
                address = value;
                return true;
            }
        } catch(Exception e) {
            ShowError(e);
        }
        address = null;
        return false;
    }

    [RelayCommand]
    public void CancelMemoryEdit() {
        IsEditingMemory = false;
    }

    [RelayCommand]
    public void ApplyMemoryEdit() {
        if (_memory is null || !TryParseMemoryAddress(MemoryEditAddress, out uint? address) || MemoryEditValue is null || !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture,  out long value)) {
            return;
        }
        _memory.LoadData(address.Value, BitConverter.GetBytes(value));
        RefreshMemoryStream();
        IsEditingMemory = false;
    }

    private Decoder? _decoder;

    private void UpdateValues(object? sender, EventArgs e) {
        ProgramExecutor?.Accept(this);
        LastUpdate = DateTime.Now;
        IsLoading = false;
    }

    [ObservableProperty]
    private StateInfo _state = new();

    [ObservableProperty]
    private CpuFlagsInfo _flags = new();

    /// <summary>
    /// Refreshing the Memory UI is taxing, and resets the Scroll Viewer.
    /// So it's done only on each Pause.
    /// </summary>
    private bool _mustRefreshMainMemory = true;

    public void VisitMainMemory(IMemory memory) {
        if (_mustRefreshMainMemory) {
            _memory = memory;
            RefreshMemoryStream();
            _mustRefreshMainMemory = false;
        }
    }
    
    [ObservableProperty]
    private EmulatedMemoryStream? _memoryStream;

    private void RefreshMemoryStream() {
        if(_memory is not null) {
            EmulatedMemoryStream memoryStream = new EmulatedMemoryStream(_memory);
            MemoryStream?.Dispose();
            MemoryStream = null;
            MemoryStream = memoryStream;
        }
    }

    public void VisitCpuState(State state) {
        if (IsLoading || !IsPaused) {
            UpdateCpuState(state);
        }

        if (IsPaused) {
            State.PropertyChanged -= OnStatePropertyChanged;
            State.PropertyChanged += OnStatePropertyChanged;
            Flags.PropertyChanged -= OnStatePropertyChanged;
            Flags.PropertyChanged += OnStatePropertyChanged;
        } else {
            State.PropertyChanged -= OnStatePropertyChanged;
            Flags.PropertyChanged -= OnStatePropertyChanged;
        }
        
        return;
        
        void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is null || e.PropertyName == null || !IsPaused || IsLoading) {
                return;
            }
            PropertyInfo? originalPropertyInfo = state.GetType().GetProperty(e.PropertyName);
            PropertyInfo? propertyInfo = sender.GetType().GetProperty(e.PropertyName);
            if (propertyInfo is not null && originalPropertyInfo is not null) {
                originalPropertyInfo.SetValue(state, propertyInfo.GetValue(sender));
            }
        }
    }

    private void UpdateCpuState(State state) {
        State.AH = state.AH;
        State.AL = state.AL;
        State.AX = state.AX;
        State.EAX = state.EAX;
        State.BH = state.BH;
        State.BL = state.BL;
        State.BX = state.BX;
        State.EBX = state.EBX;
        State.CH = state.CH;
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
        State.IpPhysicalAddress = state.IpPhysicalAddress;
        State.StackPhysicalAddress = state.StackPhysicalAddress;
        State.SegmentOverrideIndex = state.SegmentOverrideIndex;
        Flags.AuxiliaryFlag = state.AuxiliaryFlag;
        Flags.CarryFlag = state.CarryFlag;
        Flags.DirectionFlag = state.DirectionFlag;
        Flags.InterruptFlag = state.InterruptFlag;
        Flags.OverflowFlag = state.OverflowFlag;
        Flags.ParityFlag = state.ParityFlag;
        Flags.ZeroFlag = state.ZeroFlag;
        Flags.ContinueZeroFlag = state.ContinueZeroFlagValue;
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

    private bool _needToUpdateDisassembly;
    
    public void VisitCpu(Cpu cpu) {
        if (!IsPaused) {
            _needToUpdateDisassembly = true;
        }
        if (IsLoading || _needToUpdateDisassembly) {
            UpdateDisassembly(cpu);
            _needToUpdateDisassembly = false;
        }
    }

    [ObservableProperty]
    private MidiInfo _midi = new();

    public void VisitExternalMidiDevice(Midi midi) {
        Midi.LastPortRead = midi.LastPortRead;
        Midi.LastPortWritten = midi.LastPortWritten;
        Midi.LastPortWrittenValue = midi.LastPortWrittenValue;
    }

    [RelayCommand]
    public void Pause() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = true;
    }

    [RelayCommand]
    public void Continue() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = false;
    }

    private Cpu? _cpu;

    private void UpdateDisassembly(Cpu cpu) {
        if (_memory is null) {
            return;
        }
        _cpu = cpu;
        uint currentIp = cpu.State.IpPhysicalAddress;
        CodeReader codeReader = CreateCodeReader(_memory, out EmulatedMemoryStream emulatedMemoryStream);
        
        // The CPU instruction bitness might have changed (jump between 16 bit and 32 bit code), so we recreate the decoder each time.
        _decoder = Decoder.Create(16, codeReader, currentIp,
            DecoderOptions.Loadall286 | DecoderOptions.Loadall386);
        Instructions.Clear();

        int byteOffset = 0;
        emulatedMemoryStream.Position = currentIp - 10;
        while (Instructions.Count < 50) {
            var instructionAddress = emulatedMemoryStream.Position;
            _decoder.Decode(out Instruction instruction);
            CpuInstructionInfo cpuInstrunction = new CpuInstructionInfo {
                Instruction = instruction,
                Address = (uint)instructionAddress,
                Length = instruction.Length,
                IP16 = instruction.IP16,
                IP32 = instruction.IP32,
                MemorySegment = instruction.MemorySegment,
                SegmentPrefix = instruction.SegmentPrefix,
                IsStackInstruction = instruction.IsStackInstruction,
                IsIPRelativeMemoryOperand = instruction.IsIPRelativeMemoryOperand,
                IPRelativeMemoryAddress = instruction.IPRelativeMemoryAddress,
                SegmentedAddress =
                    ConvertUtils.ToSegmentedAddressRepresentation(_cpu.State.CS, (ushort)(_cpu.State.IP + byteOffset - 10)),
                FlowControl = instruction.FlowControl,
                Bytes = $"{Convert.ToHexString(_memory.GetData((uint)instructionAddress, (uint)instruction.Length))}"
            };
            if (instructionAddress == currentIp) {
                cpuInstrunction.IsCsIp = true;
            }
            Instructions.Add(cpuInstrunction);
            byteOffset += instruction.Length;
        }
        emulatedMemoryStream.Dispose();
    }

    private CodeReader CreateCodeReader(IMemory memory, out EmulatedMemoryStream emulatedMemoryStream) {
        emulatedMemoryStream = new EmulatedMemoryStream(memory);
        CodeReader codeReader = new StreamCodeReader(emulatedMemoryStream);
        return codeReader;
    }

    public void ShowColorPalette() {
        SelectedTab = 4;
    }
}