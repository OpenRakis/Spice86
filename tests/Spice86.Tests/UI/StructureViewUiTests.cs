namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using AvaloniaHex.Document;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.DataModels;
using Spice86.Views;

using Structurizer;
using Structurizer.Types;

public class StructureViewUiTests : BreakpointUiTestBase {
    private const string HeaderSource = """
typedef struct Vec2 {
    short x;
    short y;
} Vec2;

typedef struct PlayerState {
    char name[8];
    unsigned short hp;
    short mana;
    long score;
    Vec2 pos;
} PlayerState;
""";

    private const int PlayerStateSize = 20;
    private const int AbsolutePlayerAddress = 0x0040;
    private const ushort SegmentedPlayerSegment = 0x0100;
    private const ushort SegmentedPlayerOffset = 0x0020;
    private const int SegmentedPlayerLinearAddress = (SegmentedPlayerSegment << 4) + SegmentedPlayerOffset;

    [AvaloniaFact]
    public void StructureView_ParsesProvidedHeader_AndExposesStructureInAutoCompleteSource() {
        // Arrange
        byte[] programImage = CreateProgramImage();
        using StructureViewModel viewModel = CreateAddressableViewModel(programImage, out _);

        // Act
        StructType? playerState = viewModel.AvailableStructures.FirstOrDefault(s => s.Name == "PlayerState");
        bool filterMatch = viewModel.StructFilter("player", playerState);

        // Assert
        playerState.Should().NotBeNull();
        filterMatch.Should().BeTrue();
        viewModel.IsAddressableMemory.Should().BeTrue();
    }

    [AvaloniaFact]
    public void StructureView_UsesAbsoluteAddress_AndHydratesMembersFromProgramMemory() {
        // Arrange
        byte[] programImage = CreateProgramImage();
        using StructureViewModel viewModel = CreateAddressableViewModel(programImage, out _);
        StructureView view = new() { DataContext = viewModel };
        ShowWindowAndWait(view);

        StructType playerState = RequireStructure(viewModel, "PlayerState");

        // Act
        viewModel.MemoryAddress = $"0x{AbsolutePlayerAddress:X4}";
        viewModel.SelectedStructure = playerState;
        ProcessUiEvents();

        // Assert
        StructureMemberNode nameNode = RequireRootMember(viewModel, "name");
        StructureMemberNode hpNode = RequireRootMember(viewModel, "hp");
        StructureMemberNode manaNode = RequireRootMember(viewModel, "mana");
        StructureMemberNode scoreNode = RequireRootMember(viewModel, "score");
        StructureMemberNode posNode = RequireRootMember(viewModel, "pos");

        nameNode.Value.Should().Be("\"INDYJONE\"");
        hpNode.Value.Should().Be("300 [0x012C]");
        manaNode.Value.Should().Be("-42 [0xFFD6]");
        scoreNode.Value.Should().Be("123456 [0x0001E240]");
        posNode.Value.Should().Be("0xFF3803E8");
        viewModel.StructureMemory.Length.Should().Be((ulong)PlayerStateSize);

        view.Close();
    }

    [AvaloniaFact]
    public void StructureView_UsesSegmentOffsetAddress_AndReadsExpectedRecord() {
        // Arrange
        byte[] programImage = CreateProgramImage();
        using StructureViewModel viewModel = CreateAddressableViewModel(programImage, out State state);
        StructureView view = new() { DataContext = viewModel };
        ShowWindowAndWait(view);

        state.DS = SegmentedPlayerSegment;
        StructType playerState = RequireStructure(viewModel, "PlayerState");

        // Act
        viewModel.MemoryAddress =
            $"{SegmentedPlayerSegment:X4}:{SegmentedPlayerOffset:X4}";
        viewModel.SelectedStructure = playerState;
        ProcessUiEvents();

        // Assert
        StructureMemberNode nameNode = RequireRootMember(viewModel, "name");
        StructureMemberNode hpNode = RequireRootMember(viewModel, "hp");
        StructureMemberNode scoreNode = RequireRootMember(viewModel, "score");
        StructureMemberNode posNode = RequireRootMember(viewModel, "pos");

        nameNode.Value.Should().Be("\"MARIONAA\"");
        hpNode.Value.Should().Be("255 [0x00FF]");
        scoreNode.Value.Should().Be("98765 [0x000181CD]");
        posNode.Value.Should().Be("0x0309FFF6");

        view.Close();
    }

    [AvaloniaFact]
    public void StructureView_RefreshesHydratedValues_WhenPauseEventIsRaised() {
        // Arrange
        byte[] programImage = CreateProgramImage();
        using StructureViewModel viewModel = CreateAddressableViewModel(programImage, out _, out Memory memory, out PauseHandler pauseHandler);
        StructType playerState = RequireStructure(viewModel, "PlayerState");

        viewModel.MemoryAddress = $"0x{AbsolutePlayerAddress:X4}";
        viewModel.SelectedStructure = playerState;
        ProcessUiEvents();

        StructureMemberNode initialHpNode = RequireRootMember(viewModel, "hp");
        initialHpNode.Value.Should().Be("300 [0x012C]");

        // Act
        memory.UInt16[(uint)(AbsolutePlayerAddress + 8)] = 512;
        pauseHandler.RequestPause("structure-view-ui-test-refresh");
        ProcessUiEvents();

        // Assert
        StructureMemberNode refreshedHpNode = RequireRootMember(viewModel, "hp");
        refreshedHpNode.Value.Should().Be("512 [0x0200]");
    }

    [AvaloniaFact]
    public void StructureView_UsesSelectionSliceProgram_AsNonAddressableDocument() {
        // Arrange
        byte[] programImage = CreateProgramImage();
        byte[] slice = new byte[PlayerStateSize];
        Buffer.BlockCopy(programImage, AbsolutePlayerAddress, slice, 0, slice.Length);

        using StructureViewModel viewModel = CreateNonAddressableViewModel(slice);
        StructureView view = new() { DataContext = viewModel };
        ShowWindowAndWait(view);

        StructType playerState = RequireStructure(viewModel, "PlayerState");

        // Act
        viewModel.SelectedStructure = playerState;
        ProcessUiEvents();

        // Assert
        viewModel.IsAddressableMemory.Should().BeFalse();
        StructureMemberNode nameNode = RequireRootMember(viewModel, "name");
        StructureMemberNode hpNode = RequireRootMember(viewModel, "hp");
        nameNode.Value.Should().Be("\"INDYJONE\"");
        hpNode.Value.Should().Be("300 [0x012C]");

        view.Close();
    }

    private static StructType RequireStructure(StructureViewModel viewModel, string name) {
        StructType? structure = viewModel.AvailableStructures.FirstOrDefault(s => s.Name == name);
        structure.Should().NotBeNull();
        if (structure == null) {
            throw new InvalidOperationException($"Structure '{name}' was not found.");
        }
        return structure;
    }

    private static StructureMemberNode RequireRootMember(StructureViewModel viewModel, string name) {
        StructureMemberNode? node = viewModel.StructureMembers.FirstOrDefault(m => m.Name == name);
        node.Should().NotBeNull();
        if (node == null) {
            throw new InvalidOperationException($"Root member '{name}' was not found.");
        }
        return node;
    }

    private static StructureInformation ParseHeader() {
        StructurizerSettings settings = new();
        Parser parser = new(settings);
        return parser.ParseSource(HeaderSource);
    }

    private static Hydrator CreateHydrator() {
        StructurizerSettings settings = new();
        return new Hydrator(settings);
    }

    private StructureViewModel CreateAddressableViewModel(byte[] programImage, out State state) {
        return CreateAddressableViewModel(programImage, out state, out _, out _);
    }

    private StructureViewModel CreateAddressableViewModel(byte[] programImage, out State state, out Memory memory, out PauseHandler pauseHandler) {
        state = CreateState();
        (Memory createdMemory, AddressReadWriteBreakpoints _, AddressReadWriteBreakpoints _) = CreateMemory();
        memory = createdMemory;
        memory.WriteRam(programImage, 0);

        ILoggerService logger = Substitute.For<ILoggerService>();
        pauseHandler = CreatePauseHandler(logger);
        DataMemoryDocument document = new(memory, 0, (uint)programImage.Length);

        StructureInformation info = ParseHeader();
        Hydrator hydrator = CreateHydrator();
        return new StructureViewModel(info, state, hydrator, document, pauseHandler);
    }

    private StructureViewModel CreateNonAddressableViewModel(byte[] slice) {
        State state = CreateState();
        ILoggerService logger = Substitute.For<ILoggerService>();
        PauseHandler pauseHandler = CreatePauseHandler(logger);

        StructureInformation info = ParseHeader();
        Hydrator hydrator = CreateHydrator();
        MemoryBinaryDocument document = new(slice);
        return new StructureViewModel(info, state, hydrator, document, pauseHandler);
    }

    private static byte[] CreateProgramImage() {
        byte[] image = new byte[0x2000];
        byte[] firstPlayer = CreatePlayerStateRecord("INDYJONE", 300, -42, 123456, 1000, -200);
        byte[] secondPlayer = CreatePlayerStateRecord("MARIONAA", 255, 90, 98765, -10, 777);

        Buffer.BlockCopy(firstPlayer, 0, image, AbsolutePlayerAddress, firstPlayer.Length);
        Buffer.BlockCopy(secondPlayer, 0, image, SegmentedPlayerLinearAddress, secondPlayer.Length);

        return image;
    }

    private static byte[] CreatePlayerStateRecord(string name8, ushort hp, short mana, int score, short x, short y) {
        if (name8.Length != 8) {
            throw new ArgumentException("Name must contain exactly 8 characters", nameof(name8));
        }

        byte[] data = new byte[PlayerStateSize];
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name8);
        Buffer.BlockCopy(nameBytes, 0, data, 0, nameBytes.Length);

        WriteUInt16(data, 8, hp);
        WriteInt16(data, 10, mana);
        WriteInt32(data, 12, score);
        WriteInt16(data, 16, x);
        WriteInt16(data, 18, y);

        return data;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }

    private static void WriteInt16(byte[] data, int offset, short value) {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }

    private static void WriteInt32(byte[] data, int offset, int value) {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }
}
