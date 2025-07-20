using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
using Spice86.ViewModels.Services;

namespace Spice86.ViewModels;

public partial class StackMemoryViewModel : MemoryViewModel {
    public StackMemoryViewModel(IMemory memory, MemoryDataExporter memoryDataExporter, State state, Stack stack,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler,
        IMessenger messenger, IUIDispatcher uiDispatcher, ITextClipboard textClipboard,
        IHostStorageProvider storageProvider, IStructureViewModelFactory structureViewModelFactory,
        bool canCloseTab = false, string? startAddress = null,
        string? endAddress = null) :
            base(memory, memoryDataExporter, state, breakpointsViewModel, pauseHandler, messenger,
                uiDispatcher, textClipboard, storageProvider, structureViewModelFactory,
                canCloseTab, startAddress, endAddress) {
        Title = "CPU Stack Memory";
        pauseHandler.Paused += () => uiDispatcher.Post(() => UpdateStackMemoryViewModel(this, stack),
            DispatcherPriority.Background);
    }
    private static void UpdateStackMemoryViewModel(MemoryViewModel stackMemoryViewModel, Stack stack) {
        //stack.PhysicalAddress is MemoryUtils.ToPhysicalAddress(state.SS, state.SP)
        stackMemoryViewModel.StartAddress = ConvertUtils.ToHex32(stack.PhysicalAddress);
        stackMemoryViewModel.EndAddress = ConvertUtils.ToHex32(A20Gate.EndOfHighMemoryArea);
    }
}
