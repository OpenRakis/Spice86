namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Iced.Intel;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Interfaces;
using Spice86.MemoryWrappers;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

using System.ComponentModel;

public partial class DisassemblyViewModel : ViewModelBase, IInternalDebugger {
    private readonly IProgramExecutor? _programExecutor;
    private readonly IPauseStatus? _pauseStatus;
    private bool _needToUpdateDisassembly = true;
    private IMemory? _memory;

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepInstructionCommand))]
    private bool _isPaused;


    public bool IsGdbServerAvailable => _programExecutor?.IsGdbCommandHandlerAvailable is true;

    public DisassemblyViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public DisassemblyViewModel(IProgramExecutor programExecutor, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        _programExecutor = programExecutor;
        IsPaused = pauseStatus.IsPaused;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
    }

    private void OnPauseStatusChanged(object? sender, PropertyChangedEventArgs e) {
        IsPaused = _pauseStatus?.IsPaused is true;
        if(IsPaused) {
            _needToUpdateDisassembly = true;
        }
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        switch (component) {
            case IMemory mem:
                _memory = mem;
                break;
            case Cpu cpu: {
                if (_memory is not null && _needToUpdateDisassembly && IsPaused) {
                    UpdateDisassembly(cpu, _memory);
                }
                break;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void StepInstruction() {
        _programExecutor?.StepInstruction();
    }

    [RelayCommand]
    public void Pause() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = IsPaused = true;
    }

    [RelayCommand]
    public void Continue() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = IsPaused = false;
    }

    private void UpdateDisassembly(Cpu cpu, IMemory memory) {
        _needToUpdateDisassembly = false;
        uint currentIp = cpu.State.IpPhysicalAddress;
        CodeReader codeReader = CreateCodeReader(memory, out EmulatedMemoryStream emulatedMemoryStream);
        Decoder decoder = InitializeDecoder(codeReader, currentIp);
        try {
            DecodeFiftyInstructions(cpu, memory, emulatedMemoryStream, currentIp, decoder);
        } finally {
            emulatedMemoryStream.Dispose();
        }
    }

    // TODO: Infinite scroll of instructions (UI paging)
    private void DecodeFiftyInstructions(Cpu cpu, IMemory memory, EmulatedMemoryStream emulatedMemoryStream, uint currentIp,
        Decoder decoder) {
        int byteOffset = 0;
        emulatedMemoryStream.Position = currentIp - 10;
        while (Instructions.Count < 50) {
            var instructionAddress = emulatedMemoryStream.Position;
            decoder.Decode(out Instruction instruction);
            CpuInstructionInfo instructionInfo = new CpuInstructionInfo {
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
                    ConvertUtils.ToSegmentedAddressRepresentation(cpu.State.CS, (ushort)(cpu.State.IP + byteOffset - 10)),
                FlowControl = instruction.FlowControl,
                Bytes = $"{Convert.ToHexString(memory.GetData((uint)instructionAddress, (uint)instruction.Length))}"
            };
            if (instructionAddress == currentIp) {
                instructionInfo.IsCsIp = true;
            }
            Instructions.Add(instructionInfo);
            byteOffset += instruction.Length;
        }
    }

    private Decoder InitializeDecoder(CodeReader codeReader, uint currentIp)
    {
        Decoder decoder = Decoder.Create(16, codeReader, currentIp,
            DecoderOptions.Loadall286 | DecoderOptions.Loadall386);
        Instructions.Clear();
        return decoder;
    }

    private static CodeReader CreateCodeReader(IMemory memory, out EmulatedMemoryStream emulatedMemoryStream) {
        emulatedMemoryStream = new EmulatedMemoryStream(memory);
        CodeReader codeReader = new StreamCodeReader(emulatedMemoryStream);
        return codeReader;
    }
}