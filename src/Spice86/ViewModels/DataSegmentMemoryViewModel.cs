using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
using Spice86.ViewModels.Services;

namespace Spice86.ViewModels;

public partial class DataSegmentMemoryViewModel : MemoryViewModel {
    public DataSegmentMemoryViewModel(IMemory memory, MemoryDataExporter memoryDataExporter, State state,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler,
        IMessenger messenger, IUIDispatcher uiDispatcher, ITextClipboard textClipboard,
        IHostStorageProvider storageProvider, IStructureViewModelFactory structureViewModelFactory,
        bool canCloseTab = false, string? startAddress = null,
        string? endAddress = null) :
            base(memory, memoryDataExporter, state, breakpointsViewModel, pauseHandler, messenger,
                uiDispatcher, textClipboard, storageProvider, structureViewModelFactory,
                canCloseTab, startAddress, endAddress) {
        Title = "Data Segment";
        pauseHandler.Paused += () => uiDispatcher.Post(() => UpdateDataSegmentMemoryViewModel(this, state),
            DispatcherPriority.Background);
    }
    private static void UpdateDataSegmentMemoryViewModel(MemoryViewModel instance, State state) {
        instance.StartAddress = ConvertUtils.ToHex32(MemoryUtils.ToPhysicalAddress(state.DS, 0));
        instance.EndAddress = ConvertUtils.ToHex32(MemoryUtils.ToPhysicalAddress(state.DS, ushort.MaxValue));
    }
}
