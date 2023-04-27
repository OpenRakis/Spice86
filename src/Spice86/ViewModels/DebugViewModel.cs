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

        VideoCard.DacReadIndex = aeonCard.DacRegisters.ReadIndex;
        VideoCard.DacWriteIndex = aeonCard.DacRegisters.WriteIndex;

        VideoCard.AttributeControllerColorSelect = aeonCard.AttributeControllerRegisters.ColorSelect;
        VideoCard.AttributeControllerOverscanColor = aeonCard.AttributeControllerRegisters.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = aeonCard.AttributeControllerRegisters.AttributeModeControl;
        VideoCard.AttributeControllerColorPlaneEnable = aeonCard.AttributeControllerRegisters.ColorPlaneEnable;
        VideoCard.AttributeControllerHorizontalPixelPanning = aeonCard.AttributeControllerRegisters.HorizontalPixelPanning;

        VideoCard.CrtControllerOffset = aeonCard.CrtControllerRegisters.Offset;
        VideoCard.CrtControllerOverflow = aeonCard.CrtControllerRegisters.Overflow;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtControllerRegisters.CrtModeControl;
        VideoCard.CrtControllerCursorEnd = aeonCard.CrtControllerRegisters.CursorEnd;
        VideoCard.CrtControllerCursorLocation = aeonCard.CrtControllerRegisters.CursorLocation;
        VideoCard.CrtControllerCursorStart = aeonCard.CrtControllerRegisters.CursorStart;
        VideoCard.CrtControllerHorizontalTotal = aeonCard.CrtControllerRegisters.HorizontalTotal;
        VideoCard.CrtControllerLineCompare = aeonCard.CrtControllerRegisters.LineCompare;
        VideoCard.CrtControllerStartAddress = aeonCard.CrtControllerRegisters.StartAddress;
        VideoCard.CrtControllerUnderlineLocation = aeonCard.CrtControllerRegisters.UnderlineLocation;
        VideoCard.CrtControllerVerticalTotal = aeonCard.CrtControllerRegisters.VerticalTotal;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtControllerRegisters.CrtModeControl;
        VideoCard.CrtControllerEndHorizontalBlanking = aeonCard.CrtControllerRegisters.EndHorizontalBlanking;
        VideoCard.CrtControllerEndHorizontalDisplay = aeonCard.CrtControllerRegisters.EndHorizontalDisplay;
        VideoCard.CrtControllerEndHorizontalRetrace = aeonCard.CrtControllerRegisters.EndHorizontalRetrace;
        VideoCard.CrtControllerEndVerticalBlanking = aeonCard.CrtControllerRegisters.EndVerticalBlanking;
        VideoCard.CrtControllerMaximumScanLine = aeonCard.CrtControllerRegisters.MaximumScanLine;
        VideoCard.CrtControllerPresetRowScan = aeonCard.CrtControllerRegisters.PresetRowScan;
        VideoCard.CrtControllerStartHorizontalBlanking = aeonCard.CrtControllerRegisters.StartHorizontalBlanking;
        VideoCard.CrtControllerStartHorizontalRetrace = aeonCard.CrtControllerRegisters.StartHorizontalRetrace;
        VideoCard.CrtControllerStartVerticalBlanking = aeonCard.CrtControllerRegisters.StartHorizontalBlanking;
        VideoCard.CrtControllerVerticalDisplayEnd = aeonCard.CrtControllerRegisters.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = aeonCard.CrtControllerRegisters.VerticalRetraceEnd;
        VideoCard.CrtControllerVerticalRetraceStart = aeonCard.CrtControllerRegisters.VerticalRetraceStart;
        
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
        VideoCard.GraphicsGraphicsMode = aeonCard.GraphicsControllerRegisters.GraphicsMode;
        VideoCard.GraphicsMiscellaneousGraphics = aeonCard.GraphicsControllerRegisters.MiscellaneousGraphics;
        VideoCard.GraphicsReadMapSelect = aeonCard.GraphicsControllerRegisters.ReadMapSelect;
        VideoCard.GraphicsSetResetExpanded = aeonCard.GraphicsControllerRegisters.SetReset.Expanded;
        VideoCard.GraphicsColorDontCareExpanded = aeonCard.GraphicsControllerRegisters.ColorDontCare.Expanded;
        VideoCard.GraphicsEnableSetResetExpanded = aeonCard.GraphicsControllerRegisters.EnableSetReset.Expanded;

        VideoCard.SequencerReset = aeonCard.SequencerRegisters.ResetRegister.Value;
        VideoCard.SequencerClockingMode = aeonCard.SequencerRegisters.ClockingModeRegister.Value;
        VideoCard.SequencerCharacterMapSelect = aeonCard.SequencerRegisters.CharacterMapSelectRegister.Value;
        VideoCard.SequencerMapMaskExpanded = aeonCard.SequencerRegisters.MapMaskRegister.MaskValue.Expanded;
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