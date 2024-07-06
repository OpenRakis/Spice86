namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.DataTemplates;
using Spice86.MemoryWrappers;
using Spice86.Models;
using Spice86.Shared.Emulator.Memory;

using Structurizer;
using Structurizer.Types;

using System.Collections.ObjectModel;

public partial class StructureViewModel : ViewModelBase {
    private readonly Hydrator _hydrator;
    private readonly StructureInformation _structureInformation;

    [ObservableProperty]
    private SegmentedAddress? _memoryAddress;

    [ObservableProperty]
    private ObservableCollection<StructureMember> _structureMembers = [];

    [ObservableProperty]
    private IBinaryDocument _structureMemory;

    [ObservableProperty]
    private StructType? _selectedStructure;

    [ObservableProperty]
    private bool _isAddressableMemory;


    private readonly IBinaryDocument _originalMemory;

    public event EventHandler<AddressChangedMessage>? RequestScrollToAddress;


    /// <inheritdoc />
    public StructureViewModel(StructureInformation structureInformation, Hydrator hydrator, IBinaryDocument data) {
        _structureInformation = structureInformation;
        _hydrator = hydrator;
        Source = new HierarchicalTreeDataGridSource<StructureMember>(_structureMembers) {
            Columns = {
                new HierarchicalExpanderColumn<StructureMember>(new TextColumn<StructureMember, string>("Name", structureMember => structureMember.Name), structureMember => structureMember.Members),
                new TextColumn<StructureMember, string>("Type", x => x.Type.Type),
                new TextColumn<StructureMember, int>("Size", x => x.Size, null, new TextColumnOptions<StructureMember> {
                    TextAlignment = TextAlignment.Right
                }),
                new TemplateColumn<StructureMember>("Value", DataTemplateProvider.StructureMemberValueTemplate)
            }
        };
        StructureMemory = data;
        _originalMemory = data;
    }

    public HierarchicalTreeDataGridSource<StructureMember> Source { get; set; }

    public IEnumerable<StructType> AvailableStructures => _structureInformation.Structs.Values;

    /// <summary>
    /// Create the text that is displayed in the dropdown when a structure is selected.
    /// </summary>
    public AutoCompleteSelector<object>? StructItemSelector { get; } = (_, item) => ((StructType)item).Name;

    /// <summary>
    /// Filter on both the search text and the size of the structure.
    /// </summary>
    public AutoCompleteFilterPredicate<object?> StructFilter => (search, item) => search != null
        && item is StructType structType
        && structType.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        && structType.Size <= (int)StructureMemory.Length;

    partial void OnStructureMemoryChanged(IBinaryDocument? value) {
        IsAddressableMemory = value is MemoryBinaryDocument;
    }

    partial void OnSelectedStructureChanged(StructType? value) {
        Update();
    }

    partial void OnMemoryAddressChanged(SegmentedAddress? value) {
        if (value is { } address) {
            RequestScrollToAddress?.Invoke(this, new AddressChangedMessage(address.ToPhysical()));
        }
        Update();
    }

    public void Update() {
        StructureMembers.Clear();
        StructType? structType = SelectedStructure;

        if (structType is null) {
            StructureMemory = _originalMemory;

            return;
        }

        uint offset = 0;
        if (IsAddressableMemory && MemoryAddress is { } address) {
            offset = address.ToPhysical();
        }

        var data = new byte[structType.Size];
        _originalMemory.ReadBytes(offset, data);

        int index = 0;
        foreach (var typeDefinition in structType.Members) {
            var slice = new ReadOnlySpan<byte>(data, index, typeDefinition.Length);
            StructureMember member = _hydrator.Hydrate(typeDefinition, slice);
            StructureMembers.Add(member);
            index += typeDefinition.Length;
        }

        StructureMemory = new ByteArrayBinaryDocument(data.ToArray());
    }
}