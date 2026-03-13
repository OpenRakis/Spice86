namespace Spice86.ViewModels.Services;

using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;

internal sealed class MemoryTabPlugin : IDebuggerTabPlugin {
    private readonly IMemory _memory;
    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly State _state;
    private readonly Stack _stack;
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly IPauseHandler _pauseHandler;
    private readonly IMessenger _messenger;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ITextClipboard _textClipboard;
    private readonly IHostStorageProvider _storageProvider;
    private readonly IStructureViewModelFactory _structureViewModelFactory;
    private readonly ExpandedMemoryManager? _expandedMemoryManager;
    private readonly ExtendedMemoryManager? _extendedMemoryManager;

    public MemoryTabPlugin(IMemory memory, MemoryDataExporter memoryDataExporter,
        State state, Stack stack, BreakpointsViewModel breakpointsViewModel,
        IPauseHandler pauseHandler, IMessenger messenger, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IHostStorageProvider storageProvider,
        IStructureViewModelFactory structureViewModelFactory, ExpandedMemoryManager? expandedMemoryManager,
        ExtendedMemoryManager? extendedMemoryManager) {
        _memory = memory;
        _memoryDataExporter = memoryDataExporter;
        _state = state;
        _stack = stack;
        _breakpointsViewModel = breakpointsViewModel;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
        _uiDispatcher = uiDispatcher;
        _textClipboard = textClipboard;
        _storageProvider = storageProvider;
        _structureViewModelFactory = structureViewModelFactory;
        _expandedMemoryManager = expandedMemoryManager;
        _extendedMemoryManager = extendedMemoryManager;
    }

    public void Register(IDebuggerTabRegistry registry) {
        MemoryViewModel memoryViewModel = new(_memory, _memoryDataExporter, _state,
            _breakpointsViewModel, _pauseHandler, _messenger, _uiDispatcher,
            _textClipboard, _storageProvider, _structureViewModelFactory,
            canCloseTab: false);

        StackMemoryViewModel stackMemoryViewModel = new(_memory, _memoryDataExporter, _state, _stack,
            _breakpointsViewModel, _pauseHandler, _messenger, _uiDispatcher,
            _textClipboard, _storageProvider, _structureViewModelFactory,
            canCloseTab: false);

        DataSegmentMemoryViewModel dataSegmentViewModel = new(_memory, _memoryDataExporter, _state,
            _breakpointsViewModel, _pauseHandler, _messenger, _uiDispatcher,
            _textClipboard, _storageProvider, _structureViewModelFactory,
            canCloseTab: false);

        List<object> memoryViews = new() {
            memoryViewModel,
            stackMemoryViewModel,
            dataSegmentViewModel
        };

        if (_expandedMemoryManager is not null) {
            memoryViews.Add(new EmsViewModel(_expandedMemoryManager));
        }

        if (_extendedMemoryManager is not null) {
            memoryViews.Add(new XmsViewModel(_extendedMemoryManager));
        }

        registry.Add(DebuggerTabIds.MemoryViews, memoryViews);
    }
}
