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

        VideoCard.DacReadIndex = aeonCard.Dac.ReadIndex;
        VideoCard.DacWriteIndex = aeonCard.Dac.WriteIndex;

        VideoCard.AttributeControllerColorSelect = aeonCard.AttributeController.ColorSelect;
        VideoCard.AttributeControllerOverscanColor = aeonCard.AttributeController.OverscanColor;
        VideoCard.AttributeControllerAttributeModeControl = aeonCard.AttributeController.AttributeModeControl;
        VideoCard.AttributeControllerColorPlaneEnable = aeonCard.AttributeController.ColorPlaneEnable;
        VideoCard.AttributeControllerHorizontalPixelPanning = aeonCard.AttributeController.HorizontalPixelPanning;

        VideoCard.CrtControllerOffset = aeonCard.CrtController.Offset;
        VideoCard.CrtControllerOverflow = aeonCard.CrtController.Overflow;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtController.CrtModeControl;
        VideoCard.CrtControllerCursorEnd = aeonCard.CrtController.CursorEnd;
        VideoCard.CrtControllerCursorLocation = aeonCard.CrtController.CursorLocation;
        VideoCard.CrtControllerCursorStart = aeonCard.CrtController.CursorStart;
        VideoCard.CrtControllerHorizontalTotal = aeonCard.CrtController.HorizontalTotal;
        VideoCard.CrtControllerLineCompare = aeonCard.CrtController.LineCompare;
        VideoCard.CrtControllerStartAddress = aeonCard.CrtController.StartAddress;
        VideoCard.CrtControllerUnderlineLocation = aeonCard.CrtController.UnderlineLocation;
        VideoCard.CrtControllerVerticalTotal = aeonCard.CrtController.VerticalTotal;
        VideoCard.CrtControllerCrtModeControl = aeonCard.CrtController.CrtModeControl;
        VideoCard.CrtControllerEndHorizontalBlanking = aeonCard.CrtController.EndHorizontalBlanking;
        VideoCard.CrtControllerEndHorizontalDisplay = aeonCard.CrtController.EndHorizontalDisplay;
        VideoCard.CrtControllerEndHorizontalRetrace = aeonCard.CrtController.EndHorizontalRetrace;
        VideoCard.CrtControllerEndVerticalBlanking = aeonCard.CrtController.EndVerticalBlanking;
        VideoCard.CrtControllerMaximumScanLine = aeonCard.CrtController.MaximumScanLine;
        VideoCard.CrtControllerPresetRowScan = aeonCard.CrtController.PresetRowScan;
        VideoCard.CrtControllerStartHorizontalBlanking = aeonCard.CrtController.StartHorizontalBlanking;
        VideoCard.CrtControllerStartHorizontalRetrace = aeonCard.CrtController.StartHorizontalRetrace;
        VideoCard.CrtControllerStartVerticalBlanking = aeonCard.CrtController.StartHorizontalBlanking;
        VideoCard.CrtControllerVerticalDisplayEnd = aeonCard.CrtController.VerticalDisplayEnd;
        VideoCard.CrtControllerVerticalRetraceEnd = aeonCard.CrtController.VerticalRetraceEnd;
        VideoCard.CrtControllerVerticalRetraceStart = aeonCard.CrtController.VerticalRetraceStart;
        
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

        VideoCard.GraphicsBitMask = aeonCard.Graphics.BitMask;
        VideoCard.GraphicsColorCompare = aeonCard.Graphics.ColorCompare;
        VideoCard.GraphicsGraphicsMode = aeonCard.Graphics.GraphicsMode;
        VideoCard.GraphicsMiscellaneousGraphics = aeonCard.Graphics.MiscellaneousGraphics;
        VideoCard.GraphicsReadMapSelect = aeonCard.Graphics.ReadMapSelect;
        VideoCard.GraphicsSetResetExpanded = aeonCard.Graphics.SetReset.Expanded;
        VideoCard.GraphicsColorDontCareExpanded = aeonCard.Graphics.ColorDontCare.Expanded;
        VideoCard.GraphicsEnableSetResetExpanded = aeonCard.Graphics.EnableSetReset.Expanded;

        VideoCard.SequencerReset = aeonCard.Sequencer.Reset;
        VideoCard.SequencerClockingMode = aeonCard.Sequencer.ClockingMode;
        VideoCard.SequencerCharacterMapSelect = aeonCard.Sequencer.CharacterMapSelect;
        VideoCard.SequencerMapMaskExpanded = aeonCard.Sequencer.MapMask.Expanded;
        VideoCard.SequencerSequencerMemoryMode = (byte) aeonCard.Sequencer.SequencerMemoryMode;

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