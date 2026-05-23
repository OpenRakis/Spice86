namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;

/// <summary>
/// Registers DOS inspector sub-tabs (memory summary, PSP chain, MCBs).
/// </summary>
internal sealed class DosTabPlugin : IDebuggerTabPlugin {
    private readonly DosMemoryManager _memoryManager;
    private readonly ExpandedMemoryManager? _ems;
    private readonly ExtendedMemoryManager? _xms;
    private readonly DosSwappableDataArea _sda;
    private readonly IByteReaderWriter _memory;
    private readonly IPauseHandler _pauseHandler;

    public DosTabPlugin(DosMemoryManager memoryManager,
        ExpandedMemoryManager? ems,
        ExtendedMemoryManager? xms,
        DosSwappableDataArea sda,
        IByteReaderWriter memory,
        IPauseHandler pauseHandler) {
        _memoryManager = memoryManager;
        _ems = ems;
        _xms = xms;
        _sda = sda;
        _memory = memory;
        _pauseHandler = pauseHandler;
    }

    public void Register(IDebuggerTabRegistry registry) {
        DosMemorySummaryViewModel summaryVm = new(_memoryManager, _ems, _xms, _pauseHandler);
        DosPspChainViewModel pspVm = new(_memoryManager, _sda, _memory, _pauseHandler);
        DosMcbsViewModel mcbsVm = new(_memoryManager, _pauseHandler);
        DosPspGraphViewModel pspGraphVm = new(_memoryManager, _sda, _memory, _pauseHandler);
        DosMemoryOverviewViewModel overviewVm = new(_memoryManager, _ems, _xms, _pauseHandler);

        registry.AddSubTab(DebuggerTabId.DosGroup, new DebuggerSubTabViewModel(DebuggerTabId.DosMemorySummary, summaryVm));
        registry.AddSubTab(DebuggerTabId.DosGroup, new DebuggerSubTabViewModel(DebuggerTabId.DosMemoryOverview, overviewVm));
        registry.AddSubTab(DebuggerTabId.DosGroup, new DebuggerSubTabViewModel(DebuggerTabId.DosPspChain, pspVm));
        registry.AddSubTab(DebuggerTabId.DosGroup, new DebuggerSubTabViewModel(DebuggerTabId.DosMcbs, mcbsVm));
        registry.AddSubTab(DebuggerTabId.DosGroup, new DebuggerSubTabViewModel(DebuggerTabId.DosPspGraph, pspGraphVm));
    }
}
