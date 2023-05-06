namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Models.Debugging;

public partial class DebugViewModel : ObservableObject {
    [ObservableProperty]
    private MachineInfo _machine = new();
    
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();

    [ObservableProperty]
    private DateTime? _lastUpdate = null;

    private Machine? _emulatorMachine;

    private readonly DispatcherTimer _timer;
    
    public DebugViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        _timer = new DispatcherTimer();
    }

    [RelayCommand]
    public void UpdateData() => UpdateValues(this, EventArgs.Empty);
    
    public DebugViewModel(Machine machine) {
        _emulatorMachine = machine;
        _timer = new(TimeSpan.FromMilliseconds(10), DispatcherPriority.Normal, UpdateValues);
        _timer.Start();
    }

    private void UpdateValues(object? sender, EventArgs e) {
        VideoState? videoState = _emulatorMachine?.VgaRegisters;
        if (videoState is null) {
            return;
        }

        VideoCard.DacReadIndex = videoState.DacRegisters.IndexRegisterReadMode;
        VideoCard.DacWriteIndex = videoState.DacRegisters.IndexRegisterWriteMode;

        VideoCard.AttributeControllerColorSelect = videoState.AttributeControllerRegisters.ColorSelectRegister.Value;
        VideoCard.AttributeControllerOverscanColor = videoState.AttributeControllerRegisters.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.Value;
        VideoCard.AttributeControllerColorPlaneEnable = videoState.AttributeControllerRegisters.ColorPlaneEnableRegister.Value;
        VideoCard.AttributeControllerHorizontalPixelPanning = videoState.AttributeControllerRegisters.HorizontalPixelPanning;

        VideoCard.CrtControllerOffset = videoState.CrtControllerRegisters.Offset;
        VideoCard.CrtControllerOverflow = videoState.CrtControllerRegisters.OverflowRegister.Value;
        VideoCard.CrtControllerCrtModeControl = videoState.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerCursorEnd = videoState.CrtControllerRegisters.TextCursorEndRegister.Value;
        VideoCard.CrtControllerCursorLocation = (ushort)videoState.CrtControllerRegisters.TextCursorLocation;
        VideoCard.CrtControllerCursorStart = videoState.CrtControllerRegisters.TextCursorStartRegister.Value;
        VideoCard.CrtControllerHorizontalTotal = videoState.CrtControllerRegisters.HorizontalTotal;
        VideoCard.CrtControllerLineCompare = videoState.CrtControllerRegisters.LineCompare;
        VideoCard.CrtControllerStartAddress = videoState.CrtControllerRegisters.ScreenStartAddressHigh;
        VideoCard.CrtControllerUnderlineLocation = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.Value;
        VideoCard.CrtControllerVerticalTotal = videoState.CrtControllerRegisters.VerticalTotal;
        VideoCard.CrtControllerCrtModeControl = videoState.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerEndHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.Value;
        VideoCard.CrtControllerEndHorizontalDisplay = videoState.CrtControllerRegisters.HorizontalDisplayEnd;
        VideoCard.CrtControllerEndHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.Value;
        VideoCard.CrtControllerEndVerticalBlanking = videoState.CrtControllerRegisters.VerticalBlankingEnd;
        VideoCard.CrtControllerMaximumScanLine = videoState.CrtControllerRegisters.CharacterCellHeightRegister.Value;
        VideoCard.CrtControllerPresetRowScan = videoState.CrtControllerRegisters.PresetRowScanRegister.Value;
        VideoCard.CrtControllerStartHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerStartHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncStart;
        VideoCard.CrtControllerStartVerticalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerVerticalDisplayEnd = videoState.CrtControllerRegisters.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = videoState.CrtControllerRegisters.VerticalSyncEndRegister.Value;
        VideoCard.CrtControllerVerticalRetraceStart = videoState.CrtControllerRegisters.VerticalSyncStart;
        
        VideoCard.CurrentModeHeight = videoState.CurrentMode.Height;
        VideoCard.CurrentModeWidth = videoState.CurrentMode.Width;
        // VideoCard.CurrentModeStride = videoState.CurrentMode.Stride;
        // VideoCard.CurrentModeBytePanning = videoState.CurrentMode.BytePanning;
        // VideoCard.CurrentModeFontHeight = videoState.CurrentMode.FontHeight;
        VideoCard.CurrentModeBitsPerPixel = videoState.CurrentMode.BitsPerPixel;
        // VideoCard.CurrentModeHorizontalPanning = videoState.CurrentMode.HorizontalPanning;
        // VideoCard.CurrentModeIsPlanar = videoState.CurrentMode.IsPlanar;
        // VideoCard.CurrentModeLineCompare = videoState.CurrentMode.LineCompare;
        // VideoCard.CurrentModeMouseWidth = videoState.CurrentMode.MouseWidth;
        // VideoCard.CurrentModeOriginalHeight = videoState.CurrentMode.OriginalHeight;
        // VideoCard.CurrentModePixelHeight = videoState.CurrentMode.PixelHeight;
        // VideoCard.CurrentModeStartOffset = videoState.CurrentMode.StartOffset;
        // VideoCard.CurrentModeActiveDisplayPage = videoState.CurrentMode.ActiveDisplayPage;
        // VideoCard.CurrentModeStartVerticalBlanking = videoState.CurrentMode.StartVerticalBlanking;
        // VideoCard.CurrentModeVideoModeType = (byte)videoState.CurrentMode.VideoModeType;

        VideoCard.GraphicsBitMask = videoState.GraphicsControllerRegisters.BitMask;
        VideoCard.GraphicsColorCompare = videoState.GraphicsControllerRegisters.ColorCompare;
        VideoCard.GraphicsGraphicsMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.Value;
        VideoCard.GraphicsMiscellaneousGraphics = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.Value;
        VideoCard.GraphicsReadMapSelect = videoState.GraphicsControllerRegisters.ReadMapSelectRegister.Value;
        VideoCard.GraphicsSetResetExpanded = videoState.GraphicsControllerRegisters.SetReset.Value;
        VideoCard.GraphicsColorDontCareExpanded = videoState.GraphicsControllerRegisters.ColorDontCare;
        VideoCard.GraphicsEnableSetResetExpanded = videoState.GraphicsControllerRegisters.EnableSetReset.Value;

        VideoCard.SequencerReset = videoState.SequencerRegisters.ResetRegister.Value;
        VideoCard.SequencerClockingMode = videoState.SequencerRegisters.ClockingModeRegister.Value;
        VideoCard.SequencerCharacterMapSelect = videoState.SequencerRegisters.CharacterMapSelectRegister.Value;
        VideoCard.SequencerMapMaskExpanded = videoState.SequencerRegisters.PlaneMaskRegister.Value;
        VideoCard.SequencerSequencerMemoryMode = videoState.SequencerRegisters.MemoryModeRegister.Value;

        // VideoCard.TextConsoleHeight = videoState.TextConsole.Height;
        // VideoCard.TextConsoleWidth = videoState.TextConsole.Width;
        // VideoCard.TextConsoleAnsiEnabled = videoState.TextConsole.AnsiEnabled;
        // VideoCard.TextConsoleBackgroundColor = videoState.TextConsole.BackgroundColor;
        // VideoCard.TextConsoleCursorPosition = videoState.TextConsole.CursorPosition.ToString();
        // VideoCard.TextConsoleForegroundColor = videoState.TextConsole.ForegroundColor;

        if (_emulatorMachine is not null) {
            Machine.VideoBiosInt10HandlerIndex = _emulatorMachine.VideoBiosInt10Handler.Index;
            Machine.VideoBiosInt10HandlerInterruptHandlerSegment = _emulatorMachine.VideoBiosInt10Handler.InterruptHandlerSegment;
        }

        LastUpdate = DateTime.Now;
    }
}