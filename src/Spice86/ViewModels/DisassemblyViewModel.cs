namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Iced.Intel;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

public partial class DisassemblyViewModel : ViewModelBase {
    private bool _needToUpdateDisassembly = true;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IProgramExecutor _programExecutor;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;

    [ObservableProperty]
    private string _header = "Disassembly View";

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToCsIpCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private int _numberOfInstructionsShown = 50;

    private uint? _startAddress;
    
    public uint? StartAddress {
        get => _startAddress;
        set {
            Header = value is null ? "" : $"0x{value:X}";
            SetProperty(ref _startAddress, value);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    public DisassemblyViewModel(IProgramExecutor programExecutor, IMemory memory, State state, IPauseHandler pauseHandler, IMessenger messenger, bool canCloseTab = false) {
        _messenger = messenger;
        _memory = memory;
        _state = state;
        _programExecutor = programExecutor;
        _pauseHandler = pauseHandler;
        _isPaused = pauseHandler.IsPaused;
        pauseHandler.Pausing += OnPause;
        CanCloseTab = canCloseTab;
        if (GoToCsIpCommand.CanExecute(null) && StartAddress is null) {
            GoToCsIpCommand.Execute(null);
        }
        if (_needToUpdateDisassembly && IsPaused) {
            UpdateDisassembly();
        }
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }
    
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<DisassemblyViewModel>(this));

    private void UpdateValues(object? sender, EventArgs e) {
        if (_needToUpdateDisassembly && IsPaused) {
            UpdateDisassembly();
        }
    }

    private void OnPause() {
        IsPaused = true;
        _needToUpdateDisassembly = true;
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewDisassemblyView() {
        DisassemblyViewModel disassemblyViewModel = new(_programExecutor, _memory, _state, _pauseHandler, _messenger, canCloseTab: true) {
            IsPaused = IsPaused
        };
        _messenger.Send(new AddViewModelMessage<DisassemblyViewModel>(disassemblyViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void GoToCsIp() {
        StartAddress = _state?.IpPhysicalAddress;
        _needToUpdateDisassembly = true;
        UpdateDisassemblyCommand.Execute(null);
    }
    
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void UpdateDisassembly() {
        if(StartAddress is null) {
            return;
        }
        _needToUpdateDisassembly = false;
        CodeReader codeReader = CreateCodeReader(_memory, out CodeMemoryStream emulatedMemoryStream);
        Decoder decoder = InitializeDecoder(codeReader, StartAddress.Value);
        try {
            DecodeInstructions(_state, _memory, emulatedMemoryStream, decoder, StartAddress.Value);
        } finally {
            emulatedMemoryStream.Dispose();
        }
    }

    private void DecodeInstructions(State state, IMemory memory, CodeMemoryStream codeMemoryStream,
        Decoder decoder, uint startAddress) {
        int byteOffset = 0;
        codeMemoryStream.Position = startAddress;
        var instructions = new List<CpuInstructionInfo>();
        while (instructions.Count < NumberOfInstructionsShown) {
            long instructionAddress = codeMemoryStream.Position;
            decoder.Decode(out Instruction instruction);
            CpuInstructionInfo instructionInfo = new() {
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
                    ConvertUtils.ToSegmentedAddressRepresentation(state.CS, (ushort)(state.IP + byteOffset - 10)),
                FlowControl = instruction.FlowControl,
                Bytes = $"{Convert.ToHexString(memory.GetData((uint)instructionAddress, (uint)instruction.Length))}"
            };
            if (instructionAddress == state.IpPhysicalAddress) {
                instructionInfo.IsCsIp = true;
            }
            instructions.Add(instructionInfo);
            byteOffset += instruction.Length;
        }
        Instructions.Clear();
        Instructions.AddRange(instructions);
    }

    private Decoder InitializeDecoder(CodeReader codeReader, uint currentIp) {
        Decoder decoder = Decoder.Create(16, codeReader, currentIp,
            DecoderOptions.Loadall286 | DecoderOptions.Loadall386);
        Instructions.Clear();
        return decoder;
    }

    private static CodeReader CreateCodeReader(IMemory memory, out CodeMemoryStream codeMemoryStream) {
        codeMemoryStream = new CodeMemoryStream(memory);
        CodeReader codeReader = new StreamCodeReader(codeMemoryStream);
        return codeReader;
    }
}