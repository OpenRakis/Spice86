namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Memory;
using Spice86.DataTemplates;
using Spice86.MemoryWrappers;
using Spice86.Shared.Emulator.Memory;

using Structurizer;
using Structurizer.Types;

using System.Collections.ObjectModel;

public partial class StructureViewModel : ViewModelBase {
    private readonly Memory<byte> _data;
    private readonly Hydrator _hydrator;
    private readonly StructureInformation _structureInformation;

    private IMemory _memory;

    [ObservableProperty]
    private SegmentedAddress? _memoryAddress;

    [ObservableProperty]
    private ObservableCollection<StructureMember> _structureMembers = [];

    [ObservableProperty]
    private IBinaryDocument? _structureMemory;

    /// <inheritdoc />
    public StructureViewModel(StructureInformation structureInformation, Hydrator hydrator, IMemory memory) {
        _structureInformation = structureInformation;
        _hydrator = hydrator;
        _memory = memory;
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
    }

    public HierarchicalTreeDataGridSource<StructureMember> Source { get; set; }

    public string[] AvailableStructures => _structureInformation.Structs.Keys.ToArray();

    public void SelectStructure(string? selectedItem) {
        if (string.IsNullOrWhiteSpace(selectedItem)
            || !_structureInformation.Structs.TryGetValue(selectedItem, out StructType? structType)
            || MemoryAddress == null) {
            return;
        }

        uint physicalAddress = MemoryAddress?.ToPhysical() ?? 0;
        StructureMemory = new MemoryBinaryDocument(_memory, physicalAddress, (uint)(physicalAddress + structType.Size));

        byte[] bytes = _memory.GetData(physicalAddress, (uint)structType.Size);

        StructureMembers.Clear();
        Span<byte> data = _memory.Ram.GetSpan((int)physicalAddress, structType.Size);
        StructureMemory.ReadBytes(0, data);
        int index = 0;
        foreach (TypeDefinition typeDefinition in structType.Members) {
            Span<byte> slice = data.Slice(index, typeDefinition.Length);
            StructureMember member = _hydrator.Hydrate(typeDefinition, slice);
            StructureMembers.Add(member);
            index += typeDefinition.Length;
        }

        // Hex viewer
        StructureMemory = new ByteArrayBinaryDocument(bytes);
    }
}