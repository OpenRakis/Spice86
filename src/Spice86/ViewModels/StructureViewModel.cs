namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.DataTemplates;
using Spice86.Shared.Emulator.Memory;

using Structurizer;
using Structurizer.Types;

using System.Collections.ObjectModel;

public partial class StructureViewModel : ViewModelBase, IInternalDebugger {
    private readonly StructureInformation _structureInformation;
    private readonly Hydrator _hydrator;

    private IMemory? _memory;

    [ObservableProperty]
    private string? _memoryAddress;

    [ObservableProperty]
    private ObservableCollection<StructureMember> _structureMembers = [];

    [ObservableProperty]
    private ByteArrayBinaryDocument? _structureMemory;

    /// <inheritdoc />
    public StructureViewModel(StructureInformation structureInformation, Hydrator hydrator) {
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
    }

    public HierarchicalTreeDataGridSource<StructureMember> Source { get; set; }

    public string[] AvailableStructures => _structureInformation.Structs.Keys.ToArray();

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if (component is IMemory memory) {
            _memory ??= memory;
        }
    }

    public bool NeedsToVisitEmulator => _memory == null;

    public void SelectStructure(string? selectedItem) {
        if (string.IsNullOrWhiteSpace(selectedItem)
            || _memory == null
            || !_structureInformation.Structs.TryGetValue(selectedItem, out StructType? structType)
            || !SegmentedAddress.TryParse(MemoryAddress, out SegmentedAddress address)) {
            return;
        }

        byte[] bytes = _memory.GetData(address.ToPhysical(), (uint)structType.Size);

        StructureMembers.Clear();
        Span<byte> data = bytes.AsSpan();
        int index = 0;
        foreach (TypeDefinition typeDefinition in structType.Members) {
            Span<byte> slice = data.Slice(index, typeDefinition.Length);
            StructureMember member1 = _hydrator.Hydrate(typeDefinition, slice);
            StructureMembers.Add(member1);
            index += typeDefinition.Length;
        }

        // Hex viewer
        var document = new ByteArrayBinaryDocument(bytes);
        StructureMemory = document;
    }
}