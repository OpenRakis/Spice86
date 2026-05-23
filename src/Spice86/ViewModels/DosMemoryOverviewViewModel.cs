namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.Services;

using System.Collections.ObjectModel;

/// <summary>
/// Combined DOS memory overview: MCB bar graph + structured table summary
/// covering Conventional, HMA, XMS and EMS.
/// </summary>
public sealed partial class DosMemoryOverviewViewModel : TimerRefreshViewModelBase {
    private const int HmaSizeBytes = 65520;
    private const int EmsPageSizeBytes = 16 * 1024;

    private readonly DosMemoryManager _memoryManager;
    private readonly ExpandedMemoryManager? _ems;
    private readonly ExtendedMemoryManager? _xms;

    /// <inheritdoc />
    public override string Header => "Memory Overview";

    /// <summary>MCBs as bar-graph segments.</summary>
    public List<ConventionalMemorySegment> Segments { get; private set; } = new();

    /// <summary>Bumped whenever <see cref="Segments"/> is rebuilt.</summary>
    [ObservableProperty]
    private int _segmentsVersion;

    /// <summary>Table rows for the structured memory summary.</summary>
    public ObservableCollection<MemoryReportRow> Rows { get; } = new();

    [ObservableProperty]
    private string _conventionalSummary = string.Empty;

    [ObservableProperty]
    private long _largestFreeConventionalBytes;

    [ObservableProperty]
    private long _largestFreeXmsBytes;

    [ObservableProperty]
    private int _mcbCount;

    [ObservableProperty]
    private int _freeMcbCount;

    [ObservableProperty]
    private int _xmsHandlesAllocated;

    [ObservableProperty]
    private int _emsHandlesAllocated;

    [ObservableProperty]
    private bool _xmsLoaded;

    [ObservableProperty]
    private bool _emsLoaded;

    [ObservableProperty]
    private bool _hmaPresent;

    [ObservableProperty]
    private bool _hmaClaimed;

    /// <summary>Human-friendly HMA status (present + claimed/available, or absent).</summary>
    public string HmaStatus {
        get {
            if (!HmaPresent) {
                return "Not present";
            }
            if (HmaClaimed) {
                return "Claimed by program";
            }
            return "Available";
        }
    }

    partial void OnHmaPresentChanged(bool value) {
        OnPropertyChanged(nameof(HmaStatus));
    }

    partial void OnHmaClaimedChanged(bool value) {
        OnPropertyChanged(nameof(HmaStatus));
    }

    /// <summary>Initializes a new <see cref="DosMemoryOverviewViewModel"/>.</summary>
    public DosMemoryOverviewViewModel(DosMemoryManager memoryManager,
        ExpandedMemoryManager? ems, ExtendedMemoryManager? xms,
        IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
        _ems = ems;
        _xms = xms;
    }

    /// <inheritdoc />
    protected override void RefreshCore() {
        RefreshConventional(out long convTotal, out long convUsed, out long convFree);

        long xmsTotal = 0;
        long xmsFree = 0;
        long xmsLargestFree = 0;
        int xmsHandles = 0;
        bool xmsEnabled = _xms is not null;
        bool hmaPresent = false;
        bool hmaClaimed = false;
        if (_xms is not null) {
            xmsTotal = (long)ExtendedMemoryManager.XmsMemorySize * 1024L;
            xmsFree = _xms.TotalFreeMemory;
            xmsLargestFree = _xms.LargestFreeBlockLength;
            xmsHandles = _xms.HandlesSnapshot.Count;
            hmaPresent = true;
            hmaClaimed = _xms.IsHighMemoryAreaClaimed;
        }

        long emsTotal = 0;
        long emsFree = 0;
        int emsHandles = 0;
        bool emsEnabled = _ems is not null;
        if (_ems is not null) {
            emsTotal = (long)EmmMemory.TotalPages * EmsPageSizeBytes;
            emsFree = (long)_ems.GetFreePageCount() * EmsPageSizeBytes;
            emsHandles = _ems.EmmHandles.Count;
        }

        Rows.Clear();
        Rows.Add(new MemoryReportRow {
            Category = "Conventional",
            TotalBytes = convTotal,
            UsedBytes = convUsed,
            FreeBytes = convFree,
            Notes = $"Largest free MCB: {LargestFreeConventionalBytes:N0} bytes"
        });

        long hmaUsed;
        long hmaFree;
        string hmaNotes;
        long hmaTotal;
        if (hmaPresent) {
            hmaTotal = HmaSizeBytes;
            if (hmaClaimed) {
                hmaUsed = HmaSizeBytes;
                hmaFree = 0;
                hmaNotes = "Claimed by program";
            } else {
                hmaUsed = 0;
                hmaFree = HmaSizeBytes;
                hmaNotes = "Available";
            }
        } else {
            hmaTotal = 0;
            hmaUsed = 0;
            hmaFree = 0;
            hmaNotes = "No XMS driver";
        }
        Rows.Add(new MemoryReportRow {
            Category = "HMA",
            TotalBytes = hmaTotal,
            UsedBytes = hmaUsed,
            FreeBytes = hmaFree,
            Notes = hmaNotes
        });

        string xmsNotes;
        if (xmsEnabled) {
            xmsNotes = $"Handles: {xmsHandles} / Largest free: {xmsLargestFree:N0} bytes";
        } else {
            xmsNotes = "Driver not loaded";
        }
        Rows.Add(new MemoryReportRow {
            Category = "Extended (XMS)",
            TotalBytes = xmsTotal,
            UsedBytes = xmsTotal - xmsFree,
            FreeBytes = xmsFree,
            Notes = xmsNotes
        });

        string emsNotes;
        if (emsEnabled) {
            emsNotes = $"Handles: {emsHandles} / Page size: {EmsPageSizeBytes:N0} bytes";
        } else {
            emsNotes = "Driver not loaded";
        }
        Rows.Add(new MemoryReportRow {
            Category = "Expanded (EMS)",
            TotalBytes = emsTotal,
            UsedBytes = emsTotal - emsFree,
            FreeBytes = emsFree,
            Notes = emsNotes
        });

        long allTotal = convTotal + hmaTotal + xmsTotal + emsTotal;
        long allFree = convFree + hmaFree + xmsFree + emsFree;
        Rows.Add(new MemoryReportRow {
            Category = "Total",
            TotalBytes = allTotal,
            UsedBytes = allTotal - allFree,
            FreeBytes = allFree,
            Notes = string.Empty
        });

        XmsLoaded = xmsEnabled;
        EmsLoaded = emsEnabled;
        HmaPresent = hmaPresent;
        HmaClaimed = hmaClaimed;
        XmsHandlesAllocated = xmsHandles;
        EmsHandlesAllocated = emsHandles;
        LargestFreeXmsBytes = xmsLargestFree;
    }

    private void RefreshConventional(out long total, out long used, out long free) {
        List<ConventionalMemorySegment> segments = new();
        total = 0;
        free = 0;
        long largestFree = 0;
        int mcbs = 0;
        int frees = 0;

        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateBlocks()) {
            if (!block.IsValid) {
                break;
            }
            mcbs++;
            long sizeBytes = block.AllocationSizeInBytes;
            total += sizeBytes;

            string owner;
            if (block.IsFree) {
                frees++;
                free += sizeBytes;
                if (sizeBytes > largestFree) {
                    largestFree = sizeBytes;
                }
                owner = "(free)";
            } else {
                if (string.IsNullOrEmpty(block.Owner)) {
                    owner = "PSP 0x" + block.PspSegment.ToString("X4");
                } else {
                    owner = block.Owner;
                }
            }

            segments.Add(new ConventionalMemorySegment {
                StartSegment = block.DataBlockSegment,
                SizeBytes = sizeBytes,
                IsFree = block.IsFree,
                Owner = owner,
                IsLast = block.IsLast
            });
        }

        Segments = segments;
        used = total - free;
        LargestFreeConventionalBytes = largestFree;
        McbCount = mcbs;
        FreeMcbCount = frees;
        ConventionalSummary =
            $"MCBs: {mcbs} ({frees} free) / Total: {total:N0} B / Used: {used:N0} B / Free: {free:N0} B / Largest free: {largestFree:N0} B";
        SegmentsVersion++;
    }
}
