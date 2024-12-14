namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;
using Spice86.DataTemplates;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Shared.Emulator.Memory;

using Structurizer;
using Structurizer.Types;

/// <summary>
/// ViewModel for handling the structure view in the application. It manages the display and interaction
/// with memory structures, including selection, filtering, and updating the view based on the selected structure.
/// </summary>
public partial class StructureViewModel : ViewModelBase, IDisposable {
    private readonly Hydrator _hydrator;
    private readonly IBinaryDocument _originalMemory;
    private readonly StructureInformation? _structureInformation;

    [ObservableProperty]
    private AvaloniaList<StructType> _availableStructures;

    [ObservableProperty]
    private bool _isAddressableMemory;

    [ObservableProperty]
    private SegmentedAddress? _memoryAddress;

    [ObservableProperty]
    private StructType? _selectedStructure;

    [ObservableProperty]
    private AvaloniaList<StructureMember> _structureMembers = new() {ResetBehavior = ResetBehavior.Remove};

    [ObservableProperty]
    private IBinaryDocument _structureMemory;

    private readonly IPauseHandler _pauseHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureViewModel" /> class.
    /// </summary>
    /// <param name="structureInformation">The structure information containing available structures.</param>
    /// <param name="hydrator">The hydrator used for creating structure members from binary data.</param>
    /// <param name="data">The binary document representing the memory to be displayed and interacted with.</param>
    /// <param name="pauseHandler">The pause handler for the emulator</param>
    public StructureViewModel(StructureInformation structureInformation, Hydrator hydrator, IBinaryDocument data, IPauseHandler pauseHandler) {
        _structureInformation = structureInformation;
        _hydrator = hydrator;
        _structureMemory = data;
        _originalMemory = data;
        _availableStructures = new AvaloniaList<StructType>(structureInformation.Structs.Values);
        _isAddressableMemory = data is DataMemoryDocument;
        _pauseHandler = pauseHandler;
        _pauseHandler.Pausing += OnPausing;
        Source = InitializeSource();
    }

    /// <summary>
    /// Gets or sets the <see cref="HierarchicalTreeDataGridSource{T}" /> for the structure members.
    /// This source is used to populate the hierarchical tree data grid in the UI, allowing for the
    /// display and interaction with the structure members.
    /// </summary>
    public HierarchicalTreeDataGridSource<StructureMember> Source { get; set; }

    /// <summary>
    /// Create the text that is displayed in the textbox when a structure is selected.
    /// </summary>
    public AutoCompleteSelector<object>? StructItemSelector { get; } = (_, item) => ((StructType)item).Name;

    /// <summary>
    /// Defines a filter for the autocomplete functionality, filtering structures based on the search text and their size.
    /// </summary>
    public AutoCompleteFilterPredicate<object?> StructFilter => (search, item) => search != null
        && item is StructType structType
        && structType.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        && structType.Size <= (int)StructureMemory.Length;

    /// <summary>
    /// Event that is raised to request scrolling to a specific address in the memory view.
    /// This event can be subscribed to by UI components or other parts of the application
    /// that are responsible for displaying the memory content. When raised, it provides
    /// an <see cref="AddressChangedMessage" /> containing the physical address to scroll to,
    /// allowing the subscriber to react and adjust the view accordingly.
    /// </summary>
    public event EventHandler<AddressChangedMessage>? RequestScrollToAddress;

    /// <summary>
    /// Handles changes to the structure information, updating the available structures and re-selecting the previously
    /// selected structure if it still exists.
    /// </summary>
    /// <param name="sender">The sender of the event. Not used in this method.</param>
    /// <param name="e">The event arguments. Not used in this method.</param>
    public void OnStructureInformationChanged(object? sender, EventArgs e) {
        if (_structureInformation == null) {
            AvailableStructures.Clear();
            SelectedStructure = null;

            return;
        }
        AvailableStructures = new AvaloniaList<StructType>(_structureInformation.Structs.Values);
        SelectedStructure = AvailableStructures.FirstOrDefault(structType => structType.Name.Equals(SelectedStructure?.Name));
    }

    private HierarchicalTreeDataGridSource<StructureMember> InitializeSource() {
        var nameColumn = new TextColumn<StructureMember, string>("Name", structureMember => structureMember.Name);

        return new HierarchicalTreeDataGridSource<StructureMember>(StructureMembers) {
            Columns = {
                new HierarchicalExpanderColumn<StructureMember>(nameColumn, structureMember => structureMember.Members),
                new TextColumn<StructureMember, string>("Type", x => x.Type.Type),
                new TextColumn<StructureMember, int>("Size", x => x.Size, null, new TextColumnOptions<StructureMember> {
                    TextAlignment = TextAlignment.Right
                }),
                new TemplateColumn<StructureMember>("Value", DataTemplateProvider.StructureMemberValueTemplate)
            }
        };
    }

    partial void OnSelectedStructureChanged(StructType? value) {
        if (value is null) {
            StructureMemory = _originalMemory;
            if (MemoryAddress is { } address) {
                RequestScrollToAddress?.Invoke(this, new AddressChangedMessage(address.ToPhysical()));
            }
        }
        Update();
    }

    partial void OnMemoryAddressChanged(SegmentedAddress? value) {
        if (value is { } address) {
            RequestScrollToAddress?.Invoke(this, new AddressChangedMessage(address.ToPhysical()));
        }
        Update();
    }

    /// <summary>
    /// Update the view when the application is paused.
    /// </summary>
    private void OnPausing() {
        Update();
    }

    /// <summary>
    /// Updates the view based on the current selection and state, such as the selected structure and memory address.
    /// It refreshes the structure members displayed according to the selected structure.
    /// </summary>
    private void Update() {
        StructureMembers.Clear();

        if (SelectedStructure is null) {
            return;
        }

        // Calculate the offset into the viewed memory.
        uint offset = IsAddressableMemory && MemoryAddress is { } address
            ? address.ToPhysical()
            : 0;

        byte[] data = new byte[SelectedStructure.Size];
        _originalMemory.ReadBytes(offset, data);

        List<StructureMember> members = PopulateMembers(SelectedStructure.Members, data);
        StructureMembers.AddRange(members);

        // This "zooms" the hex view to the selected structure data.
        StructureMemory = new MemoryBinaryDocument(data);
    }

    private List<StructureMember> PopulateMembers(IEnumerable<TypeDefinition> selectedStructure, byte[] data) {
        int index = 0;
        var members = new List<StructureMember>();
        foreach (TypeDefinition typeDefinition in selectedStructure) {
            var slice = new ReadOnlySpan<byte>(data, index, typeDefinition.Length);
            StructureMember member = _hydrator.Hydrate(typeDefinition, slice);
            members.Add(member);
            index += typeDefinition.Length;
        }

        return members;
    }

    /// <summary>
    /// Disposes of the resources used by the <see cref="StructureViewModel" />.
    /// </summary>
    public void Dispose() {
        _pauseHandler.Pausing -= OnPausing;
        Source.Dispose();
        GC.SuppressFinalize(this);
    }
}