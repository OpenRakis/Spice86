namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System.Collections.ObjectModel;

/// <summary>
/// DOS MCB (Memory Control Block) inspector.
/// </summary>
public sealed partial class DosMcbsViewModel : TimerRefreshViewModelBase {
    private readonly DosMemoryManager _memoryManager;

    /// <inheritdoc />
    public override string Header => "DOS MCBs";

    /// <summary>All MCBs in enumeration order.</summary>
    public ObservableCollection<DosMcbInfo> Items { get; } = new();

    /// <summary>Bar-graph blocks mirroring <see cref="Items"/>, suitable for proportional rendering.</summary>
    public ObservableCollection<DosMcbBarItem> Blocks { get; } = new();

    [ObservableProperty]
    private DosMcbInfo? _selectedItem;

    [ObservableProperty]
    private long _totalConventionalBytes;

    [ObservableProperty]
    private long _freeConventionalBytes;

    [ObservableProperty]
    private long _usedConventionalBytes;

    [ObservableProperty]
    private long _largestFreeBlockBytes;

    [ObservableProperty]
    private int _totalMcbCount;

    [ObservableProperty]
    private int _freeMcbCount;

    /// <summary>One-line layout summary suitable for a status header.</summary>
    public string LayoutSummary =>
        $"Total: {TotalConventionalBytes:N0} bytes / Used: {UsedConventionalBytes:N0} / Free: {FreeConventionalBytes:N0} / Largest free MCB: {LargestFreeBlockBytes:N0} / MCBs: {TotalMcbCount} (free: {FreeMcbCount})";

    /// <summary>Initializes a new <see cref="DosMcbsViewModel"/>.</summary>
    public DosMcbsViewModel(DosMemoryManager memoryManager, IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
    }

    /// <inheritdoc />
    protected override void RefreshCore() {
        List<DosMcbInfo> built = new();
        List<DosMcbBarItem> blocks = new();
        long total = 0;
        long free = 0;
        long used = 0;
        long largestFree = 0;
        int freeCount = 0;
        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateBlocks()) {
            DosMcbInfo info = new();
            block.CopyToDosMcbInfo(info);
            built.Add(info);
            if (block.IsValid) {
                long sizeBytes = info.SizeBytes;
                total += sizeBytes;
                if (info.IsFree) {
                    free += sizeBytes;
                    freeCount++;
                    if (sizeBytes > largestFree) {
                        largestFree = sizeBytes;
                    }
                } else {
                    used += sizeBytes;
                }
                string ownerLabel;
                if (info.IsFree) {
                    ownerLabel = "(free)";
                } else if (string.IsNullOrEmpty(info.OwnerName)) {
                    ownerLabel = info.OwnerPspSegment;
                } else {
                    ownerLabel = info.OwnerName;
                }
                string tip = $"seg:{info.HeaderSegment}  size={sizeBytes:N0} bytes  owner={ownerLabel}";
                blocks.Add(new DosMcbBarItem(info.HeaderSegment, sizeBytes, info.IsFree, info.IsLast, info.OwnerName, tip));
            }
            if (!block.IsValid) {
                break;
            }
        }
        string? previouslySelected = SelectedItem?.HeaderSegment;
        Items.Clear();
        foreach (DosMcbInfo info in built) {
            Items.Add(info);
        }
        Blocks.Clear();
        foreach (DosMcbBarItem block in blocks) {
            Blocks.Add(block);
        }
        TotalConventionalBytes = total;
        FreeConventionalBytes = free;
        UsedConventionalBytes = used;
        LargestFreeBlockBytes = largestFree;
        TotalMcbCount = blocks.Count;
        FreeMcbCount = freeCount;
        OnPropertyChanged(nameof(LayoutSummary));
        if (previouslySelected is null) {
            if (Items.Count > 0) {
                SelectedItem = Items[0];
            } else {
                SelectedItem = null;
            }
        } else {
            DosMcbInfo? found = Items.FirstOrDefault(i => i.HeaderSegment == previouslySelected);
            if (found is not null) {
                SelectedItem = found;
            } else if (Items.Count > 0) {
                SelectedItem = Items[0];
            } else {
                SelectedItem = null;
            }
        }
    }
}
