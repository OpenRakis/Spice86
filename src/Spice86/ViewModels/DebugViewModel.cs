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
        AeonCard? aeonCard = _emulatorMachine?.VgaCard as AeonCard;
        if (aeonCard is null) {
            return;
        }

        VideoCard.DacReadIndex = aeonCard.DacRegisters.IndexRegisterReadMode;
        VideoCard.DacWriteIndex = aeonCard.DacRegisters.IndexRegisterWriteMode;

        VideoCard.AttributeControllerColorSelect = aeonCard.AttributeControllerRegisters.ColorSelectRegister.Value;
        VideoCard.AttributeControllerOverscanColor = aeonCard.AttributeControllerRegisters.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = aeonCard.AttributeControllerRegisters.AttributeControllerModeRegister.Value;
        VideoCard.AttributeControllerColorPlaneEnable = aeonCard.AttributeControllerRegisters.ColorPlaneEnableRegister.Value;
        VideoCard.AttributeControllerHorizontalPixelPanning = aeonCard.AttributeControllerRegisters.HorizontalPixelPanning;

        VideoCard.CrtControllerOffset = aeonCard.CrtControllerRegisters.Offset;
        VideoCard.CrtControllerOverflow = aeonCard.CrtControllerRegisters.OverflowRegister.Value;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerCursorEnd = aeonCard.CrtControllerRegisters.TextCursorEndRegister.Value;
        VideoCard.CrtControllerCursorLocation = (ushort)aeonCard.CrtControllerRegisters.TextCursorLocation;
        VideoCard.CrtControllerCursorStart = aeonCard.CrtControllerRegisters.TextCursorStartRegister.Value;
        VideoCard.CrtControllerHorizontalTotal = aeonCard.CrtControllerRegisters.HorizontalTotal;
        VideoCard.CrtControllerLineCompare = aeonCard.CrtControllerRegisters.LineCompare;
        VideoCard.CrtControllerStartAddress = aeonCard.CrtControllerRegisters.ScreenStartAddressHigh;
        VideoCard.CrtControllerUnderlineLocation = aeonCard.CrtControllerRegisters.UnderlineRowScanlineRegister.Value;
        VideoCard.CrtControllerVerticalTotal = aeonCard.CrtControllerRegisters.VerticalTotal;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtControllerRegisters.CrtModeControlRegister.Value;
        VideoCard.CrtControllerEndHorizontalBlanking = aeonCard.CrtControllerRegisters.HorizontalBlankingEndRegister.Value;
        VideoCard.CrtControllerEndHorizontalDisplay = aeonCard.CrtControllerRegisters.HorizontalDisplayEnd;
        VideoCard.CrtControllerEndHorizontalRetrace = aeonCard.CrtControllerRegisters.HorizontalSyncEndRegister.Value;
        VideoCard.CrtControllerEndVerticalBlanking = aeonCard.CrtControllerRegisters.VerticalBlankingEnd;
        VideoCard.CrtControllerMaximumScanLine = aeonCard.CrtControllerRegisters.CharacterCellHeightRegister.Value;
        VideoCard.CrtControllerPresetRowScan = aeonCard.CrtControllerRegisters.PresetRowScanRegister.Value;
        VideoCard.CrtControllerStartHorizontalBlanking = aeonCard.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerStartHorizontalRetrace = aeonCard.CrtControllerRegisters.HorizontalSyncStart;
        VideoCard.CrtControllerStartVerticalBlanking = aeonCard.CrtControllerRegisters.HorizontalBlankingStart;
        VideoCard.CrtControllerVerticalDisplayEnd = aeonCard.CrtControllerRegisters.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = aeonCard.CrtControllerRegisters.VerticalSyncEndRegister.Value;
        VideoCard.CrtControllerVerticalRetraceStart = aeonCard.CrtControllerRegisters.VerticalSyncStart;
        
        VideoCard.CurrentModeHeight = aeonCard.CurrentMode.Height;
        VideoCard.CurrentModeWidth = aeonCard.CurrentMode.Width;
        VideoCard.CurrentModeStride = aeonCard.CurrentMode.Stride;
        VideoCard.CurrentModeBytePanning = aeonCard.CurrentMode.BytePanning;
        VideoCard.CurrentModeFontHeight = aeonCard.CurrentMode.FontHeight;
        VideoCard.CurrentModeBitsPerPixel = aeonCard.CurrentMode.BitsPerPixel;
        VideoCard.CurrentModeHorizontalPanning = aeonCard.CurrentMode.HorizontalPanning;
        VideoCard.CurrentModeIsPlanar = aeonCard.CurrentMode.IsPlanar;
        VideoCard.CurrentModeLineCompare = aeonCard.CurrentMode.LineCompare;
        VideoCard.CurrentModeMouseWidth = aeonCard.CurrentMode.MouseWidth;
        VideoCard.CurrentModeOriginalHeight = aeonCard.CurrentMode.OriginalHeight;
        VideoCard.CurrentModePixelHeight = aeonCard.CurrentMode.PixelHeight;
        VideoCard.CurrentModeStartOffset = aeonCard.CurrentMode.StartOffset;
        VideoCard.CurrentModeActiveDisplayPage = aeonCard.CurrentMode.ActiveDisplayPage;
        VideoCard.CurrentModeStartVerticalBlanking = aeonCard.CurrentMode.StartVerticalBlanking;
        VideoCard.CurrentModeVideoModeType = (byte)aeonCard.CurrentMode.VideoModeType;

        VideoCard.GraphicsBitMask = aeonCard.GraphicsControllerRegisters.BitMask;
        VideoCard.GraphicsColorCompare = aeonCard.GraphicsControllerRegisters.ColorCompare;
        VideoCard.GraphicsGraphicsMode = aeonCard.GraphicsControllerRegisters.GraphicsModeRegister.Value;
        VideoCard.GraphicsMiscellaneousGraphics = aeonCard.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.Value;
        VideoCard.GraphicsReadMapSelect = aeonCard.GraphicsControllerRegisters.ReadMapSelectRegister.Value;
        VideoCard.GraphicsSetResetExpanded = aeonCard.GraphicsControllerRegisters.SetReset.Expanded;
        VideoCard.GraphicsColorDontCareExpanded = aeonCard.GraphicsControllerRegisters.ColorDontCare;
        VideoCard.GraphicsEnableSetResetExpanded = aeonCard.GraphicsControllerRegisters.EnableSetReset.Expanded;

        VideoCard.SequencerReset = aeonCard.SequencerRegisters.ResetRegister.Value;
        VideoCard.SequencerClockingMode = aeonCard.SequencerRegisters.ClockingModeRegister.Value;
        VideoCard.SequencerCharacterMapSelect = aeonCard.SequencerRegisters.CharacterMapSelectRegister.Value;
        VideoCard.SequencerMapMaskExpanded = aeonCard.SequencerRegisters.PlaneMaskRegister.Value;
        VideoCard.SequencerSequencerMemoryMode = aeonCard.SequencerRegisters.MemoryModeRegister.Value;

        VideoCard.TextConsoleHeight = aeonCard.TextConsole.Height;
        VideoCard.TextConsoleWidth = aeonCard.TextConsole.Width;
        VideoCard.TextConsoleAnsiEnabled = aeonCard.TextConsole.AnsiEnabled;
        VideoCard.TextConsoleBackgroundColor = aeonCard.TextConsole.BackgroundColor;
        VideoCard.TextConsoleCursorPosition = aeonCard.TextConsole.CursorPosition.ToString();
        VideoCard.TextConsoleForegroundColor = aeonCard.TextConsole.ForegroundColor;

        if (_emulatorMachine is not null) {
            Machine.VideoBiosInt10HandlerIndex = _emulatorMachine.VideoBiosInt10Handler.Index;
            Machine.VideoBiosInt10HandlerInterruptHandlerSegment = _emulatorMachine.VideoBiosInt10Handler.InterruptHandlerSegment;
        }

        LastUpdate = DateTime.Now;
    }
}