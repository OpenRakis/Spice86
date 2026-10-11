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

    [ObservableProperty]
    private long _totalUpperMemoryBytes;

    [ObservableProperty]
    private long _freeUpperMemoryBytes;

    [ObservableProperty]
    private long _usedUpperMemoryBytes;

    [ObservableProperty]
    private long _largestFreeUpperMemoryBlockBytes;

    [ObservableProperty]
    private int _totalUpperMemoryMcbCount;

    [ObservableProperty]
    private int _freeUpperMemoryMcbCount;

    /// <summary>One-line layout summary suitable for a status header.</summary>
    public string LayoutSummary =>
        $"Conventional: Total {TotalConventionalBytes:N0} bytes / Used {UsedConventionalBytes:N0} / Free {FreeConventionalBytes:N0} / Largest free MCB {LargestFreeBlockBytes:N0} / MCBs {TotalMcbCount} (free: {FreeMcbCount})"
        + $" / UMB: Total {TotalUpperMemoryBytes:N0} bytes / Used {UsedUpperMemoryBytes:N0} / Free {FreeUpperMemoryBytes:N0} / Largest free MCB {LargestFreeUpperMemoryBlockBytes:N0} / MCBs {TotalUpperMemoryMcbCount} (free: {FreeUpperMemoryMcbCount})";

    /// <summary>Initializes a new <see cref="DosMcbsViewModel"/>.</summary>
    public DosMcbsViewModel(DosMemoryManager memoryManager, IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
    }

    /// <inheritdoc />
    protected override void RefreshCore() {
        List<DosMcbInfo> built = new();
        List<DosMcbBarItem> blocks = new();
        HashSet<string> displayedHeaderSegments = new();
        long total = 0;
        long free = 0;
        long used = 0;
        long largestFree = 0;
        int conventionalCount = 0;
        int freeCount = 0;
        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateConventionalBlocks()) {
            AddBlockToDisplay(block, built, blocks, displayedHeaderSegments);
            if (block.IsValid) {
                DosMcbInfo info = built[^1];
                long sizeBytes = info.SizeBytes;
                total += sizeBytes;
                conventionalCount++;
                if (info.IsFree) {
                    free += sizeBytes;
                    freeCount++;
                    if (sizeBytes > largestFree) {
                        largestFree = sizeBytes;
                    }
                } else {
                    used += sizeBytes;
                }
            }
            if (!block.IsValid) {
                break;
            }
        }

        long upperMemoryTotal = 0;
        long upperMemoryFree = 0;
        long upperMemoryUsed = 0;
        long largestUpperMemoryFree = 0;
        int upperMemoryCount = 0;
        int upperMemoryFreeCount = 0;
        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateUpperMemoryBlocks()) {
            AddBlockToDisplay(block, built, blocks, displayedHeaderSegments);
            if (block.IsValid) {
                DosMcbInfo info = built[^1];
                long sizeBytes = info.SizeBytes;
                upperMemoryTotal += sizeBytes;
                upperMemoryCount++;
                if (info.IsFree) {
                    upperMemoryFree += sizeBytes;
                    upperMemoryFreeCount++;
                    if (sizeBytes > largestUpperMemoryFree) {
                        largestUpperMemoryFree = sizeBytes;
                    }
                } else {
                    upperMemoryUsed += sizeBytes;
                }
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
        TotalMcbCount = conventionalCount;
        FreeMcbCount = freeCount;
        TotalUpperMemoryBytes = upperMemoryTotal;
        FreeUpperMemoryBytes = upperMemoryFree;
        UsedUpperMemoryBytes = upperMemoryUsed;
        LargestFreeUpperMemoryBlockBytes = largestUpperMemoryFree;
        TotalUpperMemoryMcbCount = upperMemoryCount;
        FreeUpperMemoryMcbCount = upperMemoryFreeCount;
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

    private static void AddBlockToDisplay(DosMemoryControlBlock block, List<DosMcbInfo> items,
        List<DosMcbBarItem> blocks, HashSet<string> displayedHeaderSegments) {
        DosMcbInfo info = new();
        block.CopyToDosMcbInfo(info);
        if (!displayedHeaderSegments.Add(info.HeaderSegment)) {
            return;
        }

        items.Add(info);
        if (!block.IsValid) {
            return;
        }

        string ownerLabel;
        if (info.IsFree) {
            ownerLabel = "(free)";
        } else if (string.IsNullOrEmpty(info.OwnerName)) {
            ownerLabel = info.OwnerPspSegment;
        } else {
            ownerLabel = info.OwnerName;
        }
        string tip = $"seg:{info.HeaderSegment}  size={info.SizeBytes:N0} bytes  owner={ownerLabel}";
        blocks.Add(new DosMcbBarItem(info.HeaderSegment, info.SizeBytes, info.IsFree, info.IsLast,
            info.OwnerName, tip));
    }
}
