namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System.Collections.ObjectModel;

/// <summary>
/// DOS PSP chain inspector. Walks PSPs reachable from the current PSP via <see cref="DosProgramSegmentPrefix.ParentProgramSegmentPrefix"/>.
/// </summary>
public sealed partial class DosPspChainViewModel : TimerRefreshViewModelBase {
    private readonly DosMemoryManager _memoryManager;
    private readonly DosSwappableDataArea _sda;
    private readonly IByteReaderWriter _memory;

    /// <inheritdoc />
    public override string Header => "DOS PSP Chain";

    /// <summary>All PSPs in the chain from current to root.</summary>
    public ObservableCollection<DosPspInfo> Items { get; } = new();

    [ObservableProperty]
    private DosPspInfo? _selectedItem;

    /// <summary>Initializes a new <see cref="DosPspChainViewModel"/>.</summary>
    public DosPspChainViewModel(DosMemoryManager memoryManager, DosSwappableDataArea sda, IByteReaderWriter memory,
        IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
        _sda = sda;
        _memory = memory;
    }

    /// <inheritdoc />
    protected override void RefreshCore() {
        ushort currentPsp = _sda.CurrentProgramSegmentPrefix;
        Dictionary<ushort, string> mcbOwnerNameByPsp = BuildMcbOwnerNameByPsp();
        List<DosPspInfo> chain = new();
        HashSet<ushort> visited = new();
        ushort segment = currentPsp;
        int guard = 0;
        while (segment != 0 && guard < 64 && visited.Add(segment)) {
            DosProgramSegmentPrefix psp = new(_memory, MemoryUtils.ToPhysicalAddress(segment, 0));
            DosPspInfo info = new();
            string ownerName = string.Empty;
            if (mcbOwnerNameByPsp.TryGetValue(segment, out string? ownerNameFromMcb)) {
                ownerName = ownerNameFromMcb;
            }
            psp.CopyToDosPspInfo(info, segment, currentPsp, ownerName);
            chain.Add(info);
            ushort parent = psp.ParentProgramSegmentPrefix;
            if (parent == segment) {
                break;
            }
            segment = parent;
            guard++;
        }
        ushort? previouslySelected;
        if (SelectedItem is null) {
            previouslySelected = null;
        } else {
            previouslySelected = TryParseSegment(SelectedItem.Segment);
        }
        Items.Clear();
        foreach (DosPspInfo info in chain) {
            Items.Add(info);
        }
        if (previouslySelected is null) {
            if (Items.Count > 0) {
                SelectedItem = Items[0];
            } else {
                SelectedItem = null;
            }
        } else {
            DosPspInfo? found = Items.FirstOrDefault(i => TryParseSegment(i.Segment) == previouslySelected);
            if (found is not null) {
                SelectedItem = found;
            } else if (Items.Count > 0) {
                SelectedItem = Items[0];
            } else {
                SelectedItem = null;
            }
        }
    }

    private Dictionary<ushort, string> BuildMcbOwnerNameByPsp() {
        Dictionary<ushort, string> ownerNameByPsp = new();
        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateBlocks()) {
            if (!block.IsValid || block.IsFree || ownerNameByPsp.ContainsKey(block.PspSegment)) {
                continue;
            }

            string owner = block.Owner.Trim();
            if (!string.IsNullOrEmpty(owner)) {
                ownerNameByPsp[block.PspSegment] = owner;
            }
        }

        return ownerNameByPsp;
    }

    private static ushort? TryParseSegment(string text) {
        if (string.IsNullOrEmpty(text)) {
            return null;
        }
        string trimmed;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            trimmed = text[2..];
        } else {
            trimmed = text;
        }
        if (ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out ushort value)) {
            return value;
        }
        return null;
    }
}
