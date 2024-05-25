namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Iced.Intel;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.MemoryWrappers;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

using System.ComponentModel;
using System.Reflection;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger, IDebugViewModel {
    [ObservableProperty]
    private MachineInfo _machine = new();

    [ObservableProperty]
    private DateTime? _lastUpdate;

    private readonly IPauseStatus? _pauseStatus;

    [ObservableProperty]
    private bool _isLoading = true;

    private IMemory? _memory;

    public bool IsGdbServerAvailable => _programExecutor?.IsGdbCommandHandlerAvailable is true;

    [ObservableProperty]
    private MixerViewModel? _softwareMixerViewModel;

    public DebugWindowViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void StepInstruction() {
        _programExecutor?.StepInstruction();
        IsLoading = true;
        ForceUpdate();
    }

    [RelayCommand]
    public void ForceUpdate() {
        UpdateValues(this, EventArgs.Empty);
        MemoryViewModel?.UpdateBinaryDocument();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepInstructionCommand))]
    private bool _isPaused;

    private IProgramExecutor? _programExecutor;

    public IProgramExecutor? ProgramExecutor {
        get => _programExecutor;
        set {
            if (value is null || _iuiDispatcherTimerFactory is null) {
                return;
            }

            _programExecutor = value;
            PaletteViewModel = new(_iuiDispatcherTimerFactory, value);
        }
    }

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();

    [ObservableProperty]
    private int _selectedTab;

    private readonly IUIDispatcherTimerFactory? _iuiDispatcherTimerFactory;

    [ObservableProperty]
    private PaletteViewModel? _paletteViewModel;

    [ObservableProperty]
    private MemoryViewModel? _memoryViewModel;
    
    [ObservableProperty]
    private VideoCardViewModel? _videoCardViewModel;

    [ObservableProperty]
    private CpuViewModel? _cpuViewModel;

    public DebugWindowViewModel(IUIDispatcherTimerFactory iuiDispatcherTimerFactory, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        IsPaused = _pauseStatus.IsPaused;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
        iuiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        _iuiDispatcherTimerFactory = iuiDispatcherTimerFactory;
        SoftwareMixerViewModel = new(iuiDispatcherTimerFactory);
        VideoCardViewModel = new();
        CpuViewModel = new(pauseStatus);
    }

    private void OnPauseStatusChanged(object? sender, PropertyChangedEventArgs e) {
        IsPaused = _pauseStatus?.IsPaused is true;
        if (IsPaused) {
            MemoryViewModel?.UpdateBinaryDocument();
        }
    }

    private Decoder? _decoder;

    private void UpdateValues(object? sender, EventArgs e) {
        ProgramExecutor?.Accept(this);
        LastUpdate = DateTime.Now;
        IsLoading = false;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if(component is Midi externalMidiDevice) {
            VisitExternalMidiDevice(externalMidiDevice);
        }
        if(component is Cpu cpu) {
            VisitCpu(cpu);
        }
        
        CpuViewModel?.Visit(component);
        
        VideoCardViewModel?.Visit(component);

        if (component is SoftwareMixer softwareMixer) {
            VisitSoundMixer(softwareMixer);
        }
        if(component is IMemory memory) {
            VisitMemory(memory);
        }
    }

    private void VisitMemory(IMemory memory) {
        if(_pauseStatus is null) {
            return;
        }
        _memory ??= memory;
        MemoryViewModel ??= new(memory, _pauseStatus);
    }

    private void VisitExternalMidiDevice(Midi externalMidiDevice) {
        Midi.LastPortRead = externalMidiDevice.LastPortRead;
        Midi.LastPortWritten = externalMidiDevice.LastPortWritten;
        Midi.LastPortWrittenValue = externalMidiDevice.LastPortWrittenValue;
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

        _decoder ??= Decoder.Create(16, codeReader, currentIp,
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

    public void VisitSoundMixer(SoftwareMixer mixer) {
        SoftwareMixerViewModel?.VisitSoundMixer(mixer);
    }
}