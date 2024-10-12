namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;

using System;

public partial class CpuStackViewModel : ViewModelBase {
    private readonly Stack _stack;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private uint _physicalAddress;

    [ObservableProperty]
    private DataMemoryDocument _dataMemoryDocument;

    public CpuStackViewModel(Stack stack, IMemory memory, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher) {
        _stack = stack;
        IsPaused = pauseHandler.IsPaused;
        PhysicalAddress = stack.PhysicalAddress;
        DataMemoryDocument = new DataMemoryDocument(memory, stack.PhysicalAddress, memory.Length);
        pauseHandler.Pausing += () => uiDispatcher.Post(() => IsPaused = true);
        pauseHandler.Resumed += () => uiDispatcher.Post(() => {
            PhysicalAddress = stack.PhysicalAddress;
            DataMemoryDocument = new DataMemoryDocument(memory, stack.PhysicalAddress, memory.Length);
            IsPaused = false;
        });
    }
}
