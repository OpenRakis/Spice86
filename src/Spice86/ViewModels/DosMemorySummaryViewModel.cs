namespace Spice86.ViewModels;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.ValueViewModels.Debugging;

/// <summary>
/// DOS memory map summary inspector.
/// </summary>
public sealed partial class DosMemorySummaryViewModel : InspectorViewModelBase<DosMemorySummaryInfo> {
    private readonly DosMemoryManager _memoryManager;
    private readonly ExpandedMemoryManager? _ems;
    private readonly ExtendedMemoryManager? _xms;

    /// <inheritdoc />
    public override string Header => "DOS Memory Summary";

    /// <summary>Initializes a new <see cref="DosMemorySummaryViewModel"/>.</summary>
    public DosMemorySummaryViewModel(DosMemoryManager memoryManager,
        ExpandedMemoryManager? ems, ExtendedMemoryManager? xms, IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
        _ems = ems;
        _xms = xms;
    }

    /// <inheritdoc />
    protected override void RefreshInfo(DosMemorySummaryInfo info) {
        _memoryManager.CopyToDosMemorySummaryInfo(info, _ems, _xms);
    }
}
