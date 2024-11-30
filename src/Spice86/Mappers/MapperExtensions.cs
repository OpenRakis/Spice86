namespace Spice86.Mappers;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Models.Debugging;

public static class MapperExtensions {
    public static void CopyToStateInfo(this State state, StateInfo stateInfo) {
        stateInfo.AH = state.AH;
        stateInfo.AL = state.AL;
        stateInfo.AX = state.AX;
        stateInfo.EAX = state.EAX;
        stateInfo.BH = state.BH;
        stateInfo.BL = state.BL;
        stateInfo.BX = state.BX;
        stateInfo.EBX = state.EBX;
        stateInfo.CH = state.CH;
        stateInfo.CL = state.CL;
        stateInfo.CX = state.CX;
        stateInfo.ECX = state.ECX;
        stateInfo.DH = state.DH;
        stateInfo.DL = state.DL;
        stateInfo.DX = state.DX;
        stateInfo.EDX = state.EDX;
        stateInfo.DI = state.DI;
        stateInfo.EDI = state.EDI;
        stateInfo.SI = state.SI;
        stateInfo.ES = state.ES;
        stateInfo.BP = state.BP;
        stateInfo.EBP = state.EBP;
        stateInfo.SP = state.SP;
        stateInfo.ESP = state.ESP;
        stateInfo.CS = state.CS;
        stateInfo.DS = state.DS;
        stateInfo.ES = state.ES;
        stateInfo.FS = state.FS;
        stateInfo.GS = state.GS;
        stateInfo.SS = state.SS;
        stateInfo.IP = state.IP;
        stateInfo.Cycles = state.Cycles;
        stateInfo.IpPhysicalAddress = state.IpPhysicalAddress;
        stateInfo.StackPhysicalAddress = state.StackPhysicalAddress;
        stateInfo.SegmentOverrideIndex = state.SegmentOverrideIndex;
    }

    public static void CopyFlagsToStateInfo(this State state, CpuFlagsInfo cpuFlagsInfo) {
        cpuFlagsInfo.AuxiliaryFlag = state.AuxiliaryFlag;
        cpuFlagsInfo.CarryFlag = state.CarryFlag;
        cpuFlagsInfo.DirectionFlag = state.DirectionFlag;
        cpuFlagsInfo.InterruptFlag = state.InterruptFlag;
        cpuFlagsInfo.OverflowFlag = state.OverflowFlag;
        cpuFlagsInfo.ParityFlag = state.ParityFlag;
        cpuFlagsInfo.ZeroFlag = state.ZeroFlag;
        cpuFlagsInfo.ContinueZeroFlag = state.ContinueZeroFlagValue;
    }

    public static void CopyToMidiInfo(this Midi midi, MidiInfo midiInfo) {
        midiInfo.LastPortRead = midi.LastPortRead;
        midiInfo.LastPortWritten = midi.LastPortWritten;
        midiInfo.LastPortWrittenValue = midi.LastPortWrittenValue;
    }

    public static void CopyToVideoCardInfo(this IVgaRenderer vgaRenderer, VideoCardInfo videoCardInfo) {
        videoCardInfo.RendererWidth = vgaRenderer.Width;
        videoCardInfo.RendererHeight = vgaRenderer.Height;
        videoCardInfo.RendererBufferSize = vgaRenderer.BufferSize;
        videoCardInfo.LastFrameRenderTime = vgaRenderer.LastFrameRenderTime;
    }

    public static void CopyToVideoCardInfo(this IVideoState videoState, VideoCardInfo videoCardInfo) {
        try {
            videoCardInfo.GeneralMiscellaneousOutputRegister = videoState.GeneralRegisters.MiscellaneousOutput.Value;
            videoCardInfo.GeneralClockSelect = videoState.GeneralRegisters.MiscellaneousOutput.ClockSelect;
            videoCardInfo.GeneralEnableRam = videoState.GeneralRegisters.MiscellaneousOutput.EnableRam;
            videoCardInfo.GeneralVerticalSize = videoState.GeneralRegisters.MiscellaneousOutput.VerticalSize;
            videoCardInfo.GeneralHorizontalSyncPolarity = videoState.GeneralRegisters.MiscellaneousOutput.HorizontalSyncPolarity;
            videoCardInfo.GeneralVerticalSyncPolarity = videoState.GeneralRegisters.MiscellaneousOutput.VerticalSyncPolarity;
            videoCardInfo.GeneralIoAddressSelect = videoState.GeneralRegisters.MiscellaneousOutput.IoAddressSelect;
            videoCardInfo.GeneralOddPageSelect = videoState.GeneralRegisters.MiscellaneousOutput.EvenPageSelect;
            videoCardInfo.GeneralInputStatusRegister0 = videoState.GeneralRegisters.InputStatusRegister0.Value;
            videoCardInfo.GeneralCrtInterrupt = videoState.GeneralRegisters.InputStatusRegister0.CrtInterrupt;
            videoCardInfo.GeneralSwitchSense = videoState.GeneralRegisters.InputStatusRegister0.SwitchSense;
            videoCardInfo.GeneralInputStatusRegister1 = videoState.GeneralRegisters.InputStatusRegister1.Value;
            videoCardInfo.GeneralDisplayDisabled = videoState.GeneralRegisters.InputStatusRegister1.DisplayDisabled;
            videoCardInfo.GeneralVerticalRetrace = videoState.GeneralRegisters.InputStatusRegister1.VerticalRetrace;

            videoCardInfo.DacReadIndex = videoState.DacRegisters.IndexRegisterReadMode;
            videoCardInfo.DacWriteIndex = videoState.DacRegisters.IndexRegisterWriteMode;
            videoCardInfo.DacPixelMask = videoState.DacRegisters.PixelMask;
            videoCardInfo.DacData = videoState.DacRegisters.DataPeek;

            videoCardInfo.AttributeControllerColorSelect = videoState.AttributeControllerRegisters.ColorSelectRegister.Value;
            videoCardInfo.AttributeControllerOverscanColor = videoState.AttributeControllerRegisters.OverscanColor;
            videoCardInfo.AttributeControllerAttributeModeControl = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.Value;
            videoCardInfo.AttributeControllerVideoOutput45Select = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.VideoOutput45Select;
            videoCardInfo.AttributeControllerPixelWidth8 = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8;
            videoCardInfo.AttributeControllerPixelPanningCompatibility = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.PixelPanningCompatibility;
            videoCardInfo.AttributeControllerBlinkingEnabled = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled;
            videoCardInfo.AttributeControllerLineGraphicsEnabled = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.LineGraphicsEnabled;
            videoCardInfo.AttributeControllerMonochromeEmulation = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.MonochromeEmulation;
            videoCardInfo.AttributeControllerGraphicsMode = videoState.AttributeControllerRegisters.AttributeControllerModeRegister.GraphicsMode;
            videoCardInfo.AttributeControllerColorPlaneEnable = videoState.AttributeControllerRegisters.ColorPlaneEnableRegister.Value;
            videoCardInfo.AttributeControllerHorizontalPixelPanning = videoState.AttributeControllerRegisters.HorizontalPixelPanning;

            videoCardInfo.CrtControllerAddressWrap = videoState.CrtControllerRegisters.CrtModeControlRegister.AddressWrap;
            videoCardInfo.CrtControllerBytePanning = videoState.CrtControllerRegisters.PresetRowScanRegister.BytePanning;
            videoCardInfo.CrtControllerByteWordMode = videoState.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode;
            videoCardInfo.CrtControllerCharacterCellHeightRegister = videoState.CrtControllerRegisters.MaximumScanlineRegister.Value;
            videoCardInfo.CrtControllerCharacterCellHeight = videoState.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;
            videoCardInfo.CrtControllerCompatibilityModeSupport = videoState.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport;
            videoCardInfo.CrtControllerCompatibleRead = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.CompatibleRead;
            videoCardInfo.CrtControllerCountByFour = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour;
            videoCardInfo.CrtControllerCountByTwo = videoState.CrtControllerRegisters.CrtModeControlRegister.CountByTwo;
            videoCardInfo.CrtControllerCrtcScanDouble = videoState.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble;
            videoCardInfo.CrtControllerCrtModeControl = videoState.CrtControllerRegisters.CrtModeControlRegister.Value;
            videoCardInfo.CrtControllerCursorEnd = videoState.CrtControllerRegisters.TextCursorEndRegister.Value;
            videoCardInfo.CrtControllerCursorLocationHigh = videoState.CrtControllerRegisters.TextCursorLocationHigh;
            videoCardInfo.CrtControllerCursorLocationLow = videoState.CrtControllerRegisters.TextCursorLocationLow;
            videoCardInfo.CrtControllerCursorStart = videoState.CrtControllerRegisters.TextCursorStartRegister.Value;
            videoCardInfo.CrtControllerDisableTextCursor = videoState.CrtControllerRegisters.TextCursorStartRegister.DisableTextCursor;
            videoCardInfo.CrtControllerDisableVerticalInterrupt = videoState.CrtControllerRegisters.VerticalSyncEndRegister.DisableVerticalInterrupt;
            videoCardInfo.CrtControllerDisplayEnableSkew = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;
            videoCardInfo.CrtControllerDoubleWordMode = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode;
            videoCardInfo.CrtControllerEndHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingEndRegister.Value;
            videoCardInfo.CrtControllerEndHorizontalDisplay = videoState.CrtControllerRegisters.HorizontalDisplayEnd;
            videoCardInfo.CrtControllerEndHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.Value;
            videoCardInfo.CrtControllerEndVerticalBlanking = videoState.CrtControllerRegisters.VerticalBlankingEnd;
            videoCardInfo.CrtControllerHorizontalBlankingEnd = videoState.CrtControllerRegisters.HorizontalBlankingEndValue;
            videoCardInfo.CrtControllerHorizontalSyncDelay = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncDelay;
            videoCardInfo.CrtControllerHorizontalSyncEnd = videoState.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd;
            videoCardInfo.CrtControllerHorizontalTotal = videoState.CrtControllerRegisters.HorizontalTotal;
            videoCardInfo.CrtControllerLineCompareRegister = videoState.CrtControllerRegisters.LineCompare;
            videoCardInfo.CrtControllerLineCompare = videoState.CrtControllerRegisters.LineCompareValue;
            videoCardInfo.CrtControllerOffset = videoState.CrtControllerRegisters.Offset;
            videoCardInfo.CrtControllerOverflow = videoState.CrtControllerRegisters.OverflowRegister.Value;
            videoCardInfo.CrtControllerPresetRowScan = videoState.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
            videoCardInfo.CrtControllerPresetRowScanRegister = videoState.CrtControllerRegisters.PresetRowScanRegister.Value;
            videoCardInfo.CrtControllerRefreshCyclesPerScanline = videoState.CrtControllerRegisters.VerticalSyncEndRegister.RefreshCyclesPerScanline;
            videoCardInfo.CrtControllerSelectRowScanCounter = videoState.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter;
            videoCardInfo.CrtControllerStartAddress = videoState.CrtControllerRegisters.ScreenStartAddress;
            videoCardInfo.CrtControllerStartAddressHigh = videoState.CrtControllerRegisters.ScreenStartAddressHigh;
            videoCardInfo.CrtControllerStartAddressLow = videoState.CrtControllerRegisters.ScreenStartAddressLow;
            videoCardInfo.CrtControllerStartHorizontalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
            videoCardInfo.CrtControllerStartHorizontalRetrace = videoState.CrtControllerRegisters.HorizontalSyncStart;
            videoCardInfo.CrtControllerStartVerticalBlanking = videoState.CrtControllerRegisters.HorizontalBlankingStart;
            videoCardInfo.CrtControllerTextCursorEnd = videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorEnd;
            videoCardInfo.CrtControllerTextCursorLocation = videoState.CrtControllerRegisters.TextCursorLocation;
            videoCardInfo.CrtControllerTextCursorSkew = videoState.CrtControllerRegisters.TextCursorEndRegister.TextCursorSkew;
            videoCardInfo.CrtControllerTextCursorStart = videoState.CrtControllerRegisters.TextCursorStartRegister.TextCursorStart;
            videoCardInfo.CrtControllerTimingEnable = videoState.CrtControllerRegisters.CrtModeControlRegister.TimingEnable;
            videoCardInfo.CrtControllerUnderlineLocation = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.Value;
            videoCardInfo.CrtControllerUnderlineScanline = videoState.CrtControllerRegisters.UnderlineRowScanlineRegister.UnderlineScanline;
            videoCardInfo.CrtControllerVerticalBlankingStart = videoState.CrtControllerRegisters.VerticalBlankingStartValue;
            videoCardInfo.CrtControllerVerticalDisplayEnd = videoState.CrtControllerRegisters.VerticalDisplayEndValue;
            videoCardInfo.CrtControllerVerticalDisplayEndRegister = videoState.CrtControllerRegisters.VerticalDisplayEnd;
            videoCardInfo.CrtControllerVerticalRetraceEnd = videoState.CrtControllerRegisters.VerticalSyncEndRegister.Value;
            videoCardInfo.CrtControllerVerticalRetraceStart = videoState.CrtControllerRegisters.VerticalSyncStart;
            videoCardInfo.CrtControllerVerticalSyncStart = videoState.CrtControllerRegisters.VerticalSyncStartValue;
            videoCardInfo.CrtControllerVerticalTimingHalved = videoState.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved;
            videoCardInfo.CrtControllerVerticalTotal = videoState.CrtControllerRegisters.VerticalTotalValue;
            videoCardInfo.CrtControllerVerticalTotalRegister = videoState.CrtControllerRegisters.VerticalTotal;
            videoCardInfo.CrtControllerWriteProtect = videoState.CrtControllerRegisters.VerticalSyncEndRegister.WriteProtect;

            videoCardInfo.GraphicsDataRotate = videoState.GraphicsControllerRegisters.DataRotateRegister.Value;
            videoCardInfo.GraphicsRotateCount = videoState.GraphicsControllerRegisters.DataRotateRegister.RotateCount;
            videoCardInfo.GraphicsFunctionSelect = videoState.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect;
            videoCardInfo.GraphicsBitMask = videoState.GraphicsControllerRegisters.BitMask;
            videoCardInfo.GraphicsColorCompare = videoState.GraphicsControllerRegisters.ColorCompare;
            videoCardInfo.GraphicsReadMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode;
            videoCardInfo.GraphicsWriteMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode;
            videoCardInfo.GraphicsOddEven = videoState.GraphicsControllerRegisters.GraphicsModeRegister.OddEven;
            videoCardInfo.GraphicsShiftRegisterMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode;
            videoCardInfo.GraphicsIn256ColorMode = videoState.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode;
            videoCardInfo.GraphicsModeRegister = videoState.GraphicsControllerRegisters.GraphicsModeRegister.Value;
            videoCardInfo.GraphicsMiscellaneousGraphics = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.Value;
            videoCardInfo.GraphicsGraphicsMode = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode;
            videoCardInfo.GraphicsChainOddMapsToEven = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven;
            videoCardInfo.GraphicsMemoryMap = videoState.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.MemoryMap;
            videoCardInfo.GraphicsReadMapSelect = videoState.GraphicsControllerRegisters.ReadMapSelectRegister.PlaneSelect;
            videoCardInfo.GraphicsSetReset = videoState.GraphicsControllerRegisters.SetReset.Value;
            videoCardInfo.GraphicsColorDontCare = videoState.GraphicsControllerRegisters.ColorDontCare;
            videoCardInfo.GraphicsEnableSetReset = videoState.GraphicsControllerRegisters.EnableSetReset.Value;

            videoCardInfo.SequencerResetRegister = videoState.SequencerRegisters.ResetRegister.Value;
            videoCardInfo.SequencerSynchronousReset = videoState.SequencerRegisters.ResetRegister.SynchronousReset;
            videoCardInfo.SequencerAsynchronousReset = videoState.SequencerRegisters.ResetRegister.AsynchronousReset;
            videoCardInfo.SequencerClockingModeRegister = videoState.SequencerRegisters.ClockingModeRegister.Value;
            videoCardInfo.SequencerDotsPerClock = videoState.SequencerRegisters.ClockingModeRegister.DotsPerClock;
            videoCardInfo.SequencerShiftLoad = videoState.SequencerRegisters.ClockingModeRegister.ShiftLoad;
            videoCardInfo.SequencerDotClock = videoState.SequencerRegisters.ClockingModeRegister.HalfDotClock;
            videoCardInfo.SequencerShift4 = videoState.SequencerRegisters.ClockingModeRegister.Shift4;
            videoCardInfo.SequencerScreenOff = videoState.SequencerRegisters.ClockingModeRegister.ScreenOff;
            videoCardInfo.SequencerPlaneMask = videoState.SequencerRegisters.PlaneMaskRegister.Value;
            videoCardInfo.SequencerCharacterMapSelect = videoState.SequencerRegisters.CharacterMapSelectRegister.Value;
            videoCardInfo.SequencerCharacterMapA = videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapA;
            videoCardInfo.SequencerCharacterMapB = videoState.SequencerRegisters.CharacterMapSelectRegister.CharacterMapB;
            videoCardInfo.SequencerSequencerMemoryMode = videoState.SequencerRegisters.MemoryModeRegister.Value;
            videoCardInfo.SequencerExtendedMemory = videoState.SequencerRegisters.MemoryModeRegister.ExtendedMemory;
            videoCardInfo.SequencerOddEvenMode = videoState.SequencerRegisters.MemoryModeRegister.OddEvenMode;
            videoCardInfo.SequencerChain4Mode = videoState.SequencerRegisters.MemoryModeRegister.Chain4Mode;
        } catch (IndexOutOfRangeException) {
            //A read during emulation provoked an OutOfRangeException (for example, in the DacRegisters).
            // Ignore it.
        }
    }
}
