namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Mcp;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

public class McpServerToolStateTests {
    private const string TestProgramName = "add";

    [Fact]
    public async Task McpAbout_ShouldExposeDiscoverabilityMetadataAsync() {
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        JsonDocument response = await context.CallToolAsync("mcp_about", new Dictionary<string, object?>());

        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(response));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "name", out JsonElement name).Should().BeTrue();
        name.GetString().Should().Be("Spice86 MCP Server");

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "stateless", out JsonElement stateless).Should().BeTrue();
        stateless.GetBoolean().Should().BeTrue();

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "capabilityScopes", out JsonElement scopes).Should().BeTrue();
        scopes.ValueKind.Should().Be(JsonValueKind.Array);
        scopes.GetArrayLength().Should().BeGreaterThan(0);

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "toolCount", out JsonElement toolCount).Should().BeTrue();
        toolCount.GetInt32().Should().BeGreaterThan(0);
        AssertSuccessfulToolResponseContainsCpuStatus(response);
    }

    [Fact]
    public async Task InitializeAndToolsList_ShouldReturnServerInfoAndToolsAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);

        // Act
        JsonDocument initializeResponse = await context.InitializeAsync();
        JsonDocument toolsListResponse = await context.ToolsListAsync();

        // Assert
        JsonElement initializeResult = McpJsonRpcAssertions.GetJsonRpcResult(initializeResponse);
        initializeResult.TryGetProperty("protocolVersion", out JsonElement protocolVersion).Should().BeTrue();
        protocolVersion.GetString().Should().NotBeNullOrWhiteSpace();

        initializeResult.TryGetProperty("serverInfo", out JsonElement serverInfo).Should().BeTrue();
        serverInfo.TryGetProperty("name", out JsonElement serverName).Should().BeTrue();
        serverName.GetString().Should().Contain("Spice86 MCP Server");

        JsonElement toolsResult = McpJsonRpcAssertions.GetJsonRpcResult(toolsListResponse);
        toolsResult.TryGetProperty("tools", out JsonElement toolsArray).Should().BeTrue();
        toolsArray.ValueKind.Should().Be(JsonValueKind.Array);
        toolsArray.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HealthApi_ShouldReturnOkStatusAsync() {
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        JsonDocument healthResponse = await context.GetHealthAsync();

        healthResponse.RootElement.TryGetProperty("status", out JsonElement status).Should().BeTrue();
        status.GetString().Should().Be("ok");
        healthResponse.RootElement.TryGetProperty("service", out JsonElement service).Should().BeTrue();
        service.GetString().Should().Contain("Spice86 MCP Server");
    }

    [Fact]
    public async Task ToolToggle_ShouldAffectToolsCallThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        context.Services.SetToolEnabled("read_cpu_registers", false);

        // Act
        JsonDocument disabledResponse = await context.CallToolAsync("read_cpu_registers", new Dictionary<string, object?>());

        // Assert
        JsonElement disabledResult = McpJsonRpcAssertions.GetJsonRpcResult(disabledResponse);
        disabledResult.TryGetProperty("isError", out JsonElement isError).Should().BeTrue();
        isError.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.GetToolErrorMessage(disabledResult).Should().Contain("disabled");

        // Arrange
        context.Services.SetToolEnabled("read_cpu_registers", true);

        // Act
        JsonDocument enabledResponse = await context.CallToolAsync("read_cpu_registers", new Dictionary<string, object?>());

        // Assert
        JsonElement enabledResult = McpJsonRpcAssertions.GetJsonRpcResult(enabledResponse);
        enabledResult.TryGetProperty("isError", out JsonElement isEnabledError).Should().BeFalse();
        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(enabledResult);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "eax", out JsonElement _).Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(enabledResponse);
    }

    [Fact]
    public async Task MemoryTools_ShouldUseSegmentedContractThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument readResponse = await context.CallToolAsync("read_memory", new Dictionary<string, object?> {
            ["segment"] = 0,
            ["offset"] = 0,
            ["length"] = 16
        });

        JsonDocument searchResponse = await context.CallToolAsync("search_memory", new Dictionary<string, object?> {
            ["pattern"] = "CD20",
            ["startSegment"] = 0,
            ["startOffset"] = 0,
            ["length"] = 65536,
            ["limit"] = 10
        });

        // Assert
        JsonElement readResult = McpJsonRpcAssertions.GetJsonRpcResult(readResponse);
        JsonElement readStructuredContent = McpJsonRpcAssertions.GetStructuredContent(readResult);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(readStructuredContent, "address", out JsonElement readAddress).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(readAddress, "segment", out JsonElement readSegment).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(readAddress, "offset", out JsonElement readOffset).Should().BeTrue();
        readSegment.GetInt32().Should().Be(0);
        readOffset.GetInt32().Should().Be(0);

        JsonElement searchResult = McpJsonRpcAssertions.GetJsonRpcResult(searchResponse);
        JsonElement searchStructuredContent = McpJsonRpcAssertions.GetStructuredContent(searchResult);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(searchStructuredContent, "startAddress", out JsonElement startAddress).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(startAddress, "segment", out JsonElement startSegment).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(startAddress, "offset", out JsonElement startOffset).Should().BeTrue();
        startSegment.GetInt32().Should().Be(0);
        startOffset.GetInt32().Should().Be(0);
        AssertSuccessfulToolResponseContainsCpuStatus(readResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(searchResponse);
    }

    [Fact]
    public async Task WriteMemory_ShouldWriteCpuAddressableMemoryThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument writeResponse = await context.CallToolAsync("write_memory", new Dictionary<string, object?> {
            ["segment"] = 0,
            ["offset"] = 0x500,
            ["data"] = "B16B00B5"
        });
        JsonDocument readResponse = await context.CallToolAsync("read_memory", new Dictionary<string, object?> {
            ["segment"] = 0,
            ["offset"] = 0x500,
            ["length"] = 4
        });

        // Assert
        JsonElement writeResult = McpJsonRpcAssertions.GetJsonRpcResult(writeResponse);
        writeResult.TryGetProperty("isError", out JsonElement writeIsError).Should().BeFalse();

        JsonElement readStructuredContent = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readResponse));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(readStructuredContent, "data", out JsonElement readData).Should().BeTrue();
        readData.GetString().Should().Be("B16B00B5");
        AssertSuccessfulToolResponseContainsCpuStatus(writeResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(readResponse);
    }

    [Fact]
    public async Task ToolErrors_ShouldSurfaceDetailedMessagesThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument badReadMemoryResponse = await context.CallToolAsync("read_memory", new Dictionary<string, object?> {
            ["segment"] = 0,
            ["offset"] = 0,
            ["length"] = 99999
        });
        JsonDocument badSearchMemoryResponse = await context.CallToolAsync("search_memory", new Dictionary<string, object?> {
            ["pattern"] = "ABC",
            ["startSegment"] = 0,
            ["startOffset"] = 0,
            ["length"] = 64,
            ["limit"] = 10
        });

        // Assert
        JsonElement badReadMemoryResult = McpJsonRpcAssertions.GetJsonRpcResult(badReadMemoryResponse);
        badReadMemoryResult.TryGetProperty("isError", out JsonElement readMemoryIsError).Should().BeTrue();
        readMemoryIsError.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.GetToolErrorMessage(badReadMemoryResult).Should().Contain("between 1 and 4096");

        JsonElement badSearchMemoryResult = McpJsonRpcAssertions.GetJsonRpcResult(badSearchMemoryResponse);
        badSearchMemoryResult.TryGetProperty("isError", out JsonElement searchMemoryIsError).Should().BeTrue();
        searchMemoryIsError.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.GetToolErrorMessage(badSearchMemoryResult).Should().Contain("even");
    }

    [Fact]
    public async Task NonControlTool_ShouldRestoreRunningStateOnSuccessAndErrorAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        context.Services.PauseHandler.IsPaused.Should().BeFalse();

        // Act
        JsonDocument okResponse = await context.CallToolAsync("read_cpu_registers", new Dictionary<string, object?>());
        JsonDocument errorResponse = await context.CallToolAsync("read_memory", new Dictionary<string, object?> {
            ["segment"] = 0,
            ["offset"] = 0,
            ["length"] = 99999
        });

        // Assert
        McpJsonRpcAssertions.GetJsonRpcResult(okResponse).TryGetProperty("isError", out JsonElement _).Should().BeFalse();
        JsonElement errorResult = McpJsonRpcAssertions.GetJsonRpcResult(errorResponse);
        errorResult.TryGetProperty("isError", out JsonElement isError).Should().BeTrue();
        isError.GetBoolean().Should().BeTrue();
        context.Services.PauseHandler.IsPaused.Should().BeFalse();
        AssertSuccessfulToolResponseContainsCpuStatus(okResponse);
    }

    [Fact]
    public async Task PauseTool_ShouldKeepPausedStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument pauseResponse = await context.CallToolAsync("pause_emulator", new Dictionary<string, object?>());

        // Assert
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(pauseResponse)), "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        context.Services.PauseHandler.IsPaused.Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(pauseResponse);
    }

    [Fact]
    public async Task UserPausedEmulator_WhenAiSteps_ShouldAdvanceInstructionPointerAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        int initialIp = context.Services.State.IP;
        context.Services.PauseHandler.RequestPause("User requested pause before AI stepping");
        context.Services.PauseHandler.IsPaused.Should().BeTrue();

        // Act
        JsonDocument stepResponse = await context.CallToolAsync("step", new Dictionary<string, object?>());

        // Assert
        JsonElement stepResult = McpJsonRpcAssertions.GetJsonRpcResult(stepResponse);
        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(stepResult);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "cpuState", out JsonElement cpuState).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(cpuState, "ip", out JsonElement steppedIp).Should().BeTrue();
        steppedIp.GetInt32().Should().NotBe(initialIp);
        context.Services.PauseHandler.IsPaused.Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(stepResponse);
    }

    [Fact]
    public async Task UserPausedEmulator_WhenMcpResumes_ShouldUnpauseAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        context.Services.PauseHandler.RequestPause("User requested pause before MCP resume");
        context.Services.PauseHandler.IsPaused.Should().BeTrue();

        // Act
        JsonDocument resumeResponse = await context.CallToolAsync("resume_emulator", new Dictionary<string, object?>());

        // Assert
        JsonElement resumeResult = McpJsonRpcAssertions.GetJsonRpcResult(resumeResponse);
        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(resumeResult);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        context.Services.PauseHandler.IsPaused.Should().BeFalse();
        AssertSuccessfulToolResponseContainsCpuStatus(resumeResponse);
    }

    [Fact]
    public async Task QueryXmsAndReadSearchXmsMemory_ShouldExposeAllocatedBlocksThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, enableXms: true);
        await context.InitializeAsync();
        int handle = PrepareXmsBlockWithPattern(context);

        // Act
        JsonDocument queryXmsResponse = await context.CallToolAsync("query_xms", new Dictionary<string, object?>());
        JsonDocument readXmsMemoryResponse = await context.CallToolAsync("read_xms_memory", new Dictionary<string, object?> {
            ["handle"] = handle,
            ["offset"] = 0,
            ["length"] = 4
        });
        JsonDocument searchXmsMemoryResponse = await context.CallToolAsync("search_xms_memory", new Dictionary<string, object?> {
            ["handle"] = handle,
            ["pattern"] = "BEEF",
            ["startOffset"] = 0,
            ["length"] = 1024,
            ["limit"] = 10
        });

        // Assert
        AssertXmsQueryShowsAllocations(queryXmsResponse);
        AssertHexData(readXmsMemoryResponse, "DEADBEEF");
        AssertHasMatches(searchXmsMemoryResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(queryXmsResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(readXmsMemoryResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(searchXmsMemoryResponse);
    }

    [Fact]
    public async Task QueryEmsAndReadPageFrame_ShouldExposeMappingsThroughHttpAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, enableEms: true);
        await context.InitializeAsync();

        ExpandedMemoryManager? emsManager = context.Services.EmsManager;
        emsManager.Should().NotBeNull();
        if (emsManager == null) {
            return;
        }

        EmmHandle emmHandle = emsManager.AllocatePages(1);
        emmHandle.LogicalPages[0].Write(0, 0xCA);
        emmHandle.LogicalPages[0].Write(1, 0xFE);
        emmHandle.LogicalPages[0].Write(2, 0xBA);
        emmHandle.LogicalPages[0].Write(3, 0xBE);
        byte mapResult = emsManager.MapUnmapHandlePage(0, 0, emmHandle.HandleNumber);
        mapResult.Should().Be(EmmStatus.EmmNoError);

        // Act
        JsonDocument queryEmsResponse = await context.CallToolAsync("query_ems", new Dictionary<string, object?>());
        JsonDocument readEmsPageFrameResponse = await context.CallToolAsync("read_ems_page_frame", new Dictionary<string, object?> {
            ["physicalPage"] = 0,
            ["offset"] = 0,
            ["length"] = 4
        });

        // Assert
        JsonElement queryEmsStructuredContent = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(queryEmsResponse));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(queryEmsStructuredContent, "pageMappings", out JsonElement pageMappings).Should().BeTrue();
        pageMappings.ValueKind.Should().Be(JsonValueKind.Array);
        pageMappings.GetArrayLength().Should().BeGreaterThan(0);

        JsonElement readEmsPageFrameStructuredContent = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readEmsPageFrameResponse));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(readEmsPageFrameStructuredContent, "data", out JsonElement pageFrameData).Should().BeTrue();
        pageFrameData.GetString().Should().Be("CAFEBABE");
        AssertSuccessfulToolResponseContainsCpuStatus(queryEmsResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(readEmsPageFrameResponse);
    }

    [Fact]
    public async Task SearchEmsMemory_ShouldFindPatternInLogicalPageAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, enableEms: true);
        await context.InitializeAsync();

        ExpandedMemoryManager? emsManager = context.Services.EmsManager;
        emsManager.Should().NotBeNull();
        if (emsManager == null) {
            return;
        }

        EmmHandle handle = emsManager.AllocatePages(1);
        handle.LogicalPages[0].Write(0, 0xCA);
        handle.LogicalPages[0].Write(1, 0xFE);
        handle.LogicalPages[0].Write(2, 0xBA);
        handle.LogicalPages[0].Write(3, 0xBE);

        // Act
        JsonDocument searchEmsMemoryResponse = await context.CallToolAsync("search_ems_memory", new Dictionary<string, object?> {
            ["handle"] = (int)handle.HandleNumber,
            ["logicalPage"] = 0,
            ["pattern"] = "BABE",
            ["startOffset"] = 0,
            ["length"] = 256,
            ["limit"] = 10
        });

        // Assert
        AssertHasMatches(searchEmsMemoryResponse);
        AssertSuccessfulToolResponseContainsCpuStatus(searchEmsMemoryResponse);
    }

    [Fact]
    public async Task QuerySoundBlasterState_ShouldExposeConfigurationAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument soundBlaster = await context.CallToolAsync("query_sound_blaster_state", new Dictionary<string, object?>());

        // Assert
        JsonElement soundStructured = GetSuccessfulStructuredContent(soundBlaster);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(soundStructured, "sbType", out JsonElement sbType).Should().BeTrue();
        sbType.GetString().Should().NotBeNullOrWhiteSpace();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(soundStructured, "blasterString", out JsonElement blasterString).Should().BeTrue();
        blasterString.GetString().Should().Contain("A");
    }

    [Fact]
    public async Task SoundBlasterSetSpeaker_ShouldUpdateSpeakerStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, false, false, false, SbType.Sb16, OplMode.None);
        await context.InitializeAsync();

        // Act
        JsonDocument setSpeaker = await context.CallToolAsync("sound_blaster_set_speaker", new Dictionary<string, object?> {
            ["enabled"] = true
        });
        JsonDocument soundState = await context.CallToolAsync("query_sound_blaster_state", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(setSpeaker);
        JsonElement soundStructured = GetSuccessfulStructuredContent(soundState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(soundStructured, "speakerEnabled", out JsonElement speakerEnabled).Should().BeTrue();
        speakerEnabled.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task QuerySoundBlasterDspVersion_ShouldReturnConfiguredCardVersionAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, false, false, false, SbType.Sb16, OplMode.None);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_sound_blaster_dsp_version", new Dictionary<string, object?>());

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "majorVersion", out JsonElement majorVersion).Should().BeTrue();
        majorVersion.GetInt32().Should().Be(4);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "minorVersion", out JsonElement minorVersion).Should().BeTrue();
        minorVersion.GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task SoundBlasterWriteMixerRegister_ShouldAffectMixerStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, false, false, false, SbType.Sb16, OplMode.None);
        await context.InitializeAsync();

        // Act
        JsonDocument writeMixerRegister = await context.CallToolAsync("sound_blaster_write_mixer_register", new Dictionary<string, object?> {
            ["register"] = 0x30,
            ["value"] = 0x78
        });
        JsonDocument queryMixerState = await context.CallToolAsync("query_sound_blaster_mixer_state", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(writeMixerRegister);
        JsonElement structured = GetSuccessfulStructuredContent(queryMixerState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "masterLeft", out JsonElement masterLeft).Should().BeTrue();
        masterLeft.GetInt32().Should().Be(0x78);
    }

    [Fact]
    public async Task QueryOplState_ShouldExposeMixerChannelAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument opl = await context.CallToolAsync("query_opl_state", new Dictionary<string, object?>());

        // Assert
        JsonElement oplStructured = GetSuccessfulStructuredContent(opl);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(oplStructured, "mode", out JsonElement oplMode).Should().BeTrue();
        oplMode.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OplWriteRegister_ShouldSucceedAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument oplWrite = await context.CallToolAsync("opl_write_register", new Dictionary<string, object?> {
            ["register"] = 0x20,
            ["value"] = 0x01
        });

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(oplWrite);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task QueryPcSpeakerState_ShouldExposeControlAndMixerStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_pc_speaker_state", new Dictionary<string, object?>());

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "controlPort", out JsonElement controlPort).Should().BeTrue();
        controlPort.GetInt32().Should().Be(0x61);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "mixerChannelName", out JsonElement mixerChannelName).Should().BeTrue();
        mixerChannelName.GetString().Should().Be("PcSpeaker");
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "timer2GateEnabled", out JsonElement timer2GateEnabled).Should().BeTrue();
        (timer2GateEnabled.ValueKind == JsonValueKind.True || timer2GateEnabled.ValueKind == JsonValueKind.False).Should().BeTrue();
    }

    [Fact]
    public async Task PcSpeakerSetControl_ShouldUpdateControlBitsAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument setControl = await context.CallToolAsync("pc_speaker_set_control", new Dictionary<string, object?> {
            ["timer2GateEnabled"] = true,
            ["speakerOutputEnabled"] = true
        });
        JsonDocument queryState = await context.CallToolAsync("query_pc_speaker_state", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(setControl);
        JsonElement structured = GetSuccessfulStructuredContent(queryState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "timer2GateEnabled", out JsonElement timer2GateEnabled).Should().BeTrue();
        timer2GateEnabled.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "speakerOutputEnabled", out JsonElement speakerOutputEnabled).Should().BeTrue();
        speakerOutputEnabled.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task QueryMidiState_ShouldExposeBackendAndPortsAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_midi_state", new Dictionary<string, object?>());

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "deviceKind", out JsonElement deviceKind).Should().BeTrue();
        deviceKind.GetString().Should().NotBeNullOrWhiteSpace();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "dataPort", out JsonElement dataPort).Should().BeTrue();
        dataPort.GetInt32().Should().Be(0x330);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "statusPort", out JsonElement statusPort).Should().BeTrue();
        statusPort.GetInt32().Should().Be(0x331);
    }

    [Fact]
    public async Task MidiResetAndEnterUartMode_ShouldUpdateReportedStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument resetResponse = await context.CallToolAsync("midi_reset", new Dictionary<string, object?>());
        JsonDocument resetState = await context.CallToolAsync("query_midi_state", new Dictionary<string, object?>());
        JsonDocument uartResponse = await context.CallToolAsync("midi_enter_uart_mode", new Dictionary<string, object?>());
        JsonDocument uartState = await context.CallToolAsync("query_midi_state", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(resetResponse);
        JsonElement resetStructured = GetSuccessfulStructuredContent(resetState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(resetStructured, "state", out JsonElement resetMode).Should().BeTrue();
        resetMode.GetString().Should().Be("NormalMode");

        AssertToolSucceeded(uartResponse);
        JsonElement uartStructured = GetSuccessfulStructuredContent(uartState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(uartStructured, "state", out JsonElement uartMode).Should().BeTrue();
        uartMode.GetString().Should().Be("UartMode");
    }

    [Fact]
    public async Task MidiSendBytes_ShouldSucceedForShortMessageAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("midi_send_bytes", new Dictionary<string, object?> {
            ["data"] = "90 3C 40"
        });

        // Assert
        AssertToolSucceeded(response);
    }

    [Fact]
    public async Task QueryVideoStateDetailed_ShouldExposeModeAndRendererAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument video = await context.CallToolAsync("query_video_state_detailed", new Dictionary<string, object?>());

        // Assert
        JsonElement videoStructured = GetSuccessfulStructuredContent(video);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(videoStructured, "mode", out JsonElement mode).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(mode, "memoryModel", out JsonElement memoryModel).Should().BeTrue();
        memoryModel.GetString().Should().NotBeNullOrWhiteSpace();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(videoStructured, "rendererWidth", out JsonElement rendererWidth).Should().BeTrue();
        rendererWidth.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task VideoSetCursorPosition_ShouldUpdateCursorAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        JsonDocument setTextMode = await context.CallToolAsync("video_set_mode", new Dictionary<string, object?> {
            ["modeId"] = 0x03,
            ["clearVideoMemory"] = true
        });
        AssertToolSucceeded(setTextMode);

        // Act
        JsonDocument setCursor = await context.CallToolAsync("video_set_cursor_position", new Dictionary<string, object?> {
            ["page"] = 0,
            ["x"] = 0,
            ["y"] = 0
        });
        JsonDocument queryCursor = await context.CallToolAsync("query_video_cursor", new Dictionary<string, object?> {
            ["page"] = 0
        });

        // Assert
        AssertToolSucceeded(setCursor);
        JsonElement structured = GetSuccessfulStructuredContent(queryCursor);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "position", out JsonElement position).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(position, "x", out JsonElement x).Should().BeTrue();
        x.GetInt32().Should().Be(0);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(position, "y", out JsonElement y).Should().BeTrue();
        y.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task VideoWriteText_ShouldWriteCharacterAtCursorAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        JsonDocument setTextMode = await context.CallToolAsync("video_set_mode", new Dictionary<string, object?> {
            ["modeId"] = 0x03,
            ["clearVideoMemory"] = true
        });
        AssertToolSucceeded(setTextMode);
        JsonDocument setCursor = await context.CallToolAsync("video_set_cursor_position", new Dictionary<string, object?> {
            ["page"] = 0,
            ["x"] = 0,
            ["y"] = 0
        });
        AssertToolSucceeded(setCursor);

        // Act
        JsonDocument writeText = await context.CallToolAsync("video_write_text", new Dictionary<string, object?> {
            ["text"] = "M"
        });
        JsonDocument readCharacter = await context.CallToolAsync("video_read_character", new Dictionary<string, object?> {
            ["page"] = 0,
            ["x"] = 0,
            ["y"] = 0
        });

        // Assert
        AssertToolSucceeded(writeText);
        JsonElement structured = GetSuccessfulStructuredContent(readCharacter);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "character", out JsonElement characterObj).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(characterObj, "character", out JsonElement character).Should().BeTrue();
        string? characterValue = character.GetString();
        characterValue.Should().NotBeNullOrEmpty();
        if (characterValue != null) {
            characterValue.Length.Should().Be(1);
        }
    }

    [Fact]
    public async Task VideoSetMode_ShouldUpdateVideoAndBiosStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        JsonDocument pause = await context.CallToolAsync("pause_emulator", new Dictionary<string, object?>());
        AssertToolSucceeded(pause);

        // Act
        JsonDocument setMode13 = await context.CallToolAsync("video_set_mode", new Dictionary<string, object?> {
            ["modeId"] = 0x13,
            ["clearVideoMemory"] = true
        });
        JsonDocument videoState13 = await context.CallToolAsync("query_video_state_detailed", new Dictionary<string, object?>());
        JsonDocument biosState13 = await context.CallToolAsync("query_bios_data_area", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(setMode13);
        JsonElement videoStructured = GetSuccessfulStructuredContent(videoState13);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(videoStructured, "biosVideoMode", out JsonElement videoMode13).Should().BeTrue();
        videoMode13.GetInt32().Should().Be(0x13);
        JsonElement biosStructured = GetSuccessfulStructuredContent(biosState13);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(biosStructured, "videoMode", out JsonElement biosVideoMode13).Should().BeTrue();
        biosVideoMode13.GetInt32().Should().Be(0x13);
    }

    [Fact]
    public async Task VideoSetActivePage_ShouldUpdateDetailedVideoStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument setActivePage = await context.CallToolAsync("video_set_active_page", new Dictionary<string, object?> {
            ["page"] = 1
        });
        JsonDocument videoState = await context.CallToolAsync("query_video_state_detailed", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(setActivePage);
        JsonElement structured = GetSuccessfulStructuredContent(videoState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "cursor", out JsonElement cursor).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(cursor, "page", out JsonElement activePage).Should().BeTrue();
        activePage.GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task QueryVideoPalette_ShouldExposeRegistersAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument palette = await context.CallToolAsync("query_video_palette", new Dictionary<string, object?>());

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(palette);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "registers", out JsonElement registers).Should().BeTrue();
        registers.ValueKind.Should().Be(JsonValueKind.Array);
        registers.GetArrayLength().Should().Be(16);
    }

    [Fact]
    public async Task VideoWritePixelAndReadPixel_ShouldRoundTripColorAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        JsonDocument pause = await context.CallToolAsync("pause_emulator", new Dictionary<string, object?>());
        AssertToolSucceeded(pause);
        JsonDocument setMode13 = await context.CallToolAsync("video_set_mode", new Dictionary<string, object?> {
            ["modeId"] = 0x13,
            ["clearVideoMemory"] = true
        });
        AssertToolSucceeded(setMode13);

        // Act
        JsonDocument writePixel = await context.CallToolAsync("video_write_pixel", new Dictionary<string, object?> {
            ["x"] = 10,
            ["y"] = 12,
            ["color"] = 0x0C
        });
        JsonDocument readPixel = await context.CallToolAsync("video_read_pixel", new Dictionary<string, object?> {
            ["x"] = 10,
            ["y"] = 12
        });

        // Assert
        AssertToolSucceeded(writePixel);
        JsonElement structured = GetSuccessfulStructuredContent(readPixel);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "color", out JsonElement color).Should().BeTrue();
        color.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        color.GetInt32().Should().BeLessThanOrEqualTo(0xFF);
    }

    [Fact]
    public async Task QueryBiosDataArea_ShouldExposeMachineStateAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument bios = await context.CallToolAsync("query_bios_data_area", new Dictionary<string, object?>());

        // Assert
        JsonElement biosStructured = GetSuccessfulStructuredContent(bios);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(biosStructured, "conventionalMemorySizeKb", out JsonElement conventionalMemory).Should().BeTrue();
        conventionalMemory.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QueryInterruptVector_ShouldReturnInstalledDosHandlerAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, false, false, true, SbType.None, OplMode.None);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_interrupt_vector", new Dictionary<string, object?> {
            ["vectorNumber"] = 0x21
        });

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "address", out JsonElement address).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(address, "segment", out JsonElement segment).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(address, "offset", out JsonElement offset).Should().BeTrue();
        (segment.GetInt32() != 0 || offset.GetInt32() != 0).Should().BeTrue();
    }

    [Fact]
    public async Task QueryDosState_ShouldExposeDrivesAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument dos = await context.CallToolAsync("query_dos_state", new Dictionary<string, object?>());

        // Assert
        JsonElement dosStructured = GetSuccessfulStructuredContent(dos);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(dosStructured, "currentDrive", out JsonElement currentDrive).Should().BeTrue();
        currentDrive.GetString().Should().Be("C:");
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(dosStructured, "drives", out JsonElement drives).Should().BeTrue();
        drives.ValueKind.Should().Be(JsonValueKind.Array);
        drives.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DosSetDefaultDrive_ShouldUpdateCurrentDriveAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument setDrive = await context.CallToolAsync("dos_set_default_drive", new Dictionary<string, object?> {
            ["driveLetter"] = "A"
        });
        JsonDocument dosState = await context.CallToolAsync("query_dos_state", new Dictionary<string, object?>());

        // Assert
        AssertToolSucceeded(setDrive);
        JsonElement dosStructured = GetSuccessfulStructuredContent(dosState);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(dosStructured, "currentDrive", out JsonElement currentDrive).Should().BeTrue();
        currentDrive.GetString().Should().Be("A:");
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(dosStructured, "currentDriveIndex", out JsonElement currentDriveIndex).Should().BeTrue();
        currentDriveIndex.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task QueryDosCurrentDirectory_ShouldReturnCurrentDriveDirectoryAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_dos_current_directory", new Dictionary<string, object?> {
            ["driveLetter"] = ""
        });

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "drive", out JsonElement drive).Should().BeTrue();
        drive.GetString().Should().Be("C:");
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "currentDirectory", out JsonElement currentDirectory).Should().BeTrue();
        currentDirectory.GetString().Should().Be(string.Empty);
    }

    [Fact]
    public async Task DosSetCurrentDirectory_ShouldUpdateCurrentDirectoryAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument setCurrentDirectory = await context.CallToolAsync("dos_set_current_directory", new Dictionary<string, object?> {
            ["path"] = "res"
        });
        JsonDocument queryCurrentDirectory = await context.CallToolAsync("query_dos_current_directory", new Dictionary<string, object?> {
            ["driveLetter"] = "C"
        });

        // Assert
        JsonElement setStructured = GetSuccessfulStructuredContent(setCurrentDirectory);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(setStructured, "currentDirectory", out JsonElement setDirectory).Should().BeTrue();
        setDirectory.GetString().Should().BeEquivalentTo("res");
        JsonElement queryStructured = GetSuccessfulStructuredContent(queryCurrentDirectory);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(queryStructured, "currentDirectory", out JsonElement queryDirectory).Should().BeTrue();
        queryDirectory.GetString().Should().BeEquivalentTo("res");
    }

    [Fact]
    public async Task QueryDosProgramState_ShouldExposePspAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName, false, false, true, SbType.None, OplMode.None);
        await context.InitializeAsync();

        // Act
        JsonDocument response = await context.CallToolAsync("query_dos_program_state", new Dictionary<string, object?>());

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "currentProgramSegmentPrefix", out JsonElement psp).Should().BeTrue();
        psp.GetInt32().Should().BeGreaterThan(0);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "maximumOpenFiles", out JsonElement maximumOpenFiles).Should().BeTrue();
        maximumOpenFiles.GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task IntegrationSmoke_ShouldExerciseMcpStackAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act + Assert
        await AssertCoreReadToolsAsync(context);
        await AssertPauseResumeIoToolsAsync(context);
        await AssertInputAutomationToolsAsync(context);
        await AssertMemoryManagerErrorPathsAsync(context);
        await AssertBreakpointToolsAsync(context);
    }

    private static async Task AssertInputAutomationToolsAsync(McpIntegrationContext context) {
        JsonDocument keyDown = await context.CallToolAsync("send_keyboard_key", new Dictionary<string, object?> {
            ["key"] = "Escape",
            ["isPressed"] = true
        });
        JsonDocument keyUp = await context.CallToolAsync("send_keyboard_key", new Dictionary<string, object?> {
            ["key"] = "Escape",
            ["isPressed"] = false
        });
        JsonDocument mousePacket = await context.CallToolAsync("send_mouse_packet", new Dictionary<string, object?> {
            ["packetData"] = "080000"
        });

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(
            McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(keyDown)),
            "success",
            out JsonElement keyDownSuccess).Should().BeTrue();
        keyDownSuccess.GetBoolean().Should().BeTrue();

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(
            McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(keyUp)),
            "success",
            out JsonElement keyUpSuccess).Should().BeTrue();
        keyUpSuccess.GetBoolean().Should().BeTrue();

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(
            McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(mousePacket)),
            "success",
            out JsonElement mousePacketSuccess).Should().BeTrue();
        mousePacketSuccess.GetBoolean().Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(keyDown);
        AssertSuccessfulToolResponseContainsCpuStatus(keyUp);
        AssertSuccessfulToolResponseContainsCpuStatus(mousePacket);
    }

    private static int PrepareXmsBlockWithPattern(McpIntegrationContext context) {
        ExtendedMemoryManager? xmsManager = context.Services.XmsManager;
        xmsManager.Should().NotBeNull();
        if (xmsManager == null) {
            return 0;
        }

        context.Services.State.DX = 1;
        xmsManager.AllocateExtendedMemoryBlock();
        context.Services.State.AX.Should().Be(1);

        int handle = context.Services.State.DX;
        xmsManager.TryGetBlock(handle, out XmsBlock? block).Should().BeTrue();
        block.Should().NotBeNull();
        if (block == null) {
            return 0;
        }

        xmsManager.XmsRam.Write(block.Value.Offset + 0, 0xDE);
        xmsManager.XmsRam.Write(block.Value.Offset + 1, 0xAD);
        xmsManager.XmsRam.Write(block.Value.Offset + 2, 0xBE);
        xmsManager.XmsRam.Write(block.Value.Offset + 3, 0xEF);
        return handle;
    }

    private static void AssertXmsQueryShowsAllocations(JsonDocument response) {
        JsonElement structured = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(response));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "allocatedBlocks", out JsonElement blocks).Should().BeTrue();
        blocks.GetInt32().Should().BeGreaterThan(0);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "handles", out JsonElement handles).Should().BeTrue();
        handles.ValueKind.Should().Be(JsonValueKind.Array);
        handles.GetArrayLength().Should().BeGreaterThan(0);
    }

    private static void AssertHexData(JsonDocument response, [StringSyntax("Hexadecimal")] string expectedHex) {
        JsonElement structured = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(response));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "data", out JsonElement data).Should().BeTrue();
        data.GetString().Should().Be(expectedHex);
    }

    private static void AssertHasMatches(JsonDocument response) {
        JsonElement structured = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(response));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "matches", out JsonElement matches).Should().BeTrue();
        matches.ValueKind.Should().Be(JsonValueKind.Array);
        matches.GetArrayLength().Should().BeGreaterThan(0);
    }

    private static void AssertToolSucceeded(JsonDocument response) {
        JsonElement result = McpJsonRpcAssertions.GetJsonRpcResult(response);
        result.TryGetProperty("isError", out JsonElement isError).Should().BeFalse();
        AssertSuccessfulToolResponseContainsCpuStatus(response);
    }

    private static JsonElement GetSuccessfulStructuredContent(JsonDocument response) {
        AssertToolSucceeded(response);
        JsonElement result = McpJsonRpcAssertions.GetJsonRpcResult(response);
        return McpJsonRpcAssertions.GetStructuredContent(result);
    }

    private static async Task AssertCoreReadToolsAsync(McpIntegrationContext context) {
        JsonDocument readCpu = await context.CallToolAsync("read_cpu_registers", new Dictionary<string, object?>());
        JsonDocument readCfg = await context.CallToolAsync("read_cfg_cpu_graph", new Dictionary<string, object?> { ["nodeLimit"] = null });
        JsonDocument listFuncs = await context.CallToolAsync("list_functions", new Dictionary<string, object?> { ["limit"] = 10 });
        JsonDocument video = await context.CallToolAsync("get_video_state", new Dictionary<string, object?>());
        JsonDocument screenshot = await context.CallToolAsync("screenshot", new Dictionary<string, object?>());

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readCpu)), "eax", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readCfg)), "currentContextDepth", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(listFuncs)), "functions", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(video)), "width", out JsonElement _).Should().BeTrue();
        JsonElement shot = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(screenshot));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(shot, "format", out JsonElement format).Should().BeTrue();
        format.GetString().Should().Be("png");
        AssertSuccessfulToolResponseContainsCpuStatus(readCpu);
        AssertSuccessfulToolResponseContainsCpuStatus(readCfg);
        AssertSuccessfulToolResponseContainsCpuStatus(listFuncs);
        AssertSuccessfulToolResponseContainsCpuStatus(video);
        AssertSuccessfulToolResponseContainsCpuStatus(screenshot);
    }

    private static async Task AssertPauseResumeIoToolsAsync(McpIntegrationContext context) {
        JsonDocument pause = await context.CallToolAsync("pause_emulator", new Dictionary<string, object?>());
        JsonDocument readPaused = await context.CallToolAsync("read_io_port", new Dictionary<string, object?> { ["port"] = 0x3DA });
        JsonDocument writePaused = await context.CallToolAsync("write_io_port", new Dictionary<string, object?> { ["port"] = 0x80, ["value"] = 0 });
        JsonDocument stack = await context.CallToolAsync("read_stack", new Dictionary<string, object?> { ["count"] = 4 });
        JsonDocument resume = await context.CallToolAsync("resume_emulator", new Dictionary<string, object?>());
        JsonDocument readIo = await context.CallToolAsync("read_io_port", new Dictionary<string, object?> { ["port"] = 0x3DA });
        JsonDocument writeIo = await context.CallToolAsync("write_io_port", new Dictionary<string, object?> { ["port"] = 0x80, ["value"] = 0 });
        JsonDocument go = await context.CallToolAsync("go", new Dictionary<string, object?>());

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(pause)), "success", out JsonElement pauseSuccess).Should().BeTrue();
        pauseSuccess.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readPaused)), "value", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(writePaused)), "success", out JsonElement writePausedSuccess).Should().BeTrue();
        writePausedSuccess.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(stack)), "values", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(resume)), "success", out JsonElement resumeSuccess).Should().BeTrue();
        resumeSuccess.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(readIo)), "value", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(writeIo)), "success", out JsonElement writeSuccess).Should().BeTrue();
        writeSuccess.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(go)), "success", out JsonElement goSuccess).Should().BeTrue();
        goSuccess.GetBoolean().Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(pause);
        AssertSuccessfulToolResponseContainsCpuStatus(readPaused);
        AssertSuccessfulToolResponseContainsCpuStatus(writePaused);
        AssertSuccessfulToolResponseContainsCpuStatus(stack);
        AssertSuccessfulToolResponseContainsCpuStatus(resume);
        AssertSuccessfulToolResponseContainsCpuStatus(readIo);
        AssertSuccessfulToolResponseContainsCpuStatus(writeIo);
        AssertSuccessfulToolResponseContainsCpuStatus(go);
    }

    private static async Task AssertMemoryManagerErrorPathsAsync(McpIntegrationContext context) {
        JsonDocument queryEms = await context.CallToolAsync("query_ems", new Dictionary<string, object?>());
        JsonDocument readEms = await context.CallToolAsync("read_ems_memory", new Dictionary<string, object?> { ["handle"] = 9999, ["logicalPage"] = 0, ["offset"] = 0, ["length"] = 16 });
        JsonDocument queryXms = await context.CallToolAsync("query_xms", new Dictionary<string, object?>());
        JsonDocument readXms = await context.CallToolAsync("read_xms_memory", new Dictionary<string, object?> { ["handle"] = 9999, ["offset"] = 0, ["length"] = 16 });

        JsonElement queryEmsResult = McpJsonRpcAssertions.GetJsonRpcResult(queryEms);
        if (queryEmsResult.TryGetProperty("isError", out JsonElement emsError) && emsError.GetBoolean()) {
            McpJsonRpcAssertions.GetToolErrorMessage(queryEmsResult).Should().Contain("EMS");
        }
        McpJsonRpcAssertions.GetJsonRpcResult(readEms).TryGetProperty("isError", out JsonElement readEmsError).Should().BeTrue();
        readEmsError.GetBoolean().Should().BeTrue();

        JsonElement queryXmsResult = McpJsonRpcAssertions.GetJsonRpcResult(queryXms);
        if (queryXmsResult.TryGetProperty("isError", out JsonElement xmsError) && xmsError.GetBoolean()) {
            McpJsonRpcAssertions.GetToolErrorMessage(queryXmsResult).Should().Contain("XMS");
        }
        McpJsonRpcAssertions.GetJsonRpcResult(readXms).TryGetProperty("isError", out JsonElement readXmsError).Should().BeTrue();
        readXmsError.GetBoolean().Should().BeTrue();
    }

    private static async Task AssertBreakpointToolsAsync(McpIntegrationContext context) {
        JsonDocument add = await context.CallToolAsync("add_breakpoint", new Dictionary<string, object?> {
            ["address"] = 0x100,
            ["type"] = "CPU_EXECUTION_ADDRESS",
            ["condition"] = null
        });
        JsonDocument list = await context.CallToolAsync("list_breakpoints", new Dictionary<string, object?>());
        JsonDocument clear = await context.CallToolAsync("clear_breakpoints", new Dictionary<string, object?>());
        JsonDocument invalid = await context.CallToolAsync("add_breakpoint", new Dictionary<string, object?> {
            ["address"] = 0x100,
            ["type"] = "INVALID_TYPE",
            ["condition"] = null
        });

        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(add)), "id", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(list)), "breakpoints", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(clear)), "success", out JsonElement clearSuccess).Should().BeTrue();
        clearSuccess.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.GetJsonRpcResult(invalid).TryGetProperty("isError", out JsonElement invalidError).Should().BeTrue();
        invalidError.GetBoolean().Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(add);
        AssertSuccessfulToolResponseContainsCpuStatus(list);
        AssertSuccessfulToolResponseContainsCpuStatus(clear);
    }

    [Fact]
    public async Task Step_ShouldInitiateStepAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument result = await context.CallToolAsync("step", new Dictionary<string, object?>());

        // Assert
        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(result));
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "cpuState", out JsonElement cpuState).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(cpuState, "ip", out JsonElement instructionIp).Should().BeTrue();
        instructionIp.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        AssertSuccessfulToolResponseContainsCpuStatus(result);
    }

    [Fact]
    public async Task ToolsList_ShouldWorkWithoutAnySessionHeaderAsync() {
        // Arrange - stateless server: no Mcp-Session-Id is issued or expected
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();
        context.SessionId.Should().BeNull("stateless mode must not issue session IDs");

        // Act - tools/list with no session header at all must succeed
        JsonDocument toolsResponse = await context.ToolsListAsync();

        // Assert
        JsonElement toolsResult = McpJsonRpcAssertions.GetJsonRpcResult(toolsResponse);
        toolsResult.TryGetProperty("tools", out JsonElement toolsArray).Should().BeTrue();
        toolsArray.ValueKind.Should().Be(JsonValueKind.Array);
        toolsArray.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Server_ShouldNotIssueMcpSessionId_PreventingHandshake404sAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);

        // Act
        await context.InitializeAsync();

        // Assert - a stateless server must never issue Mcp-Session-Id.
        // Issuing a session ID without guaranteeing the full
        // initialize → notifications/initialized handshake is completed
        // before ANY subsequent request causes 404 in practice when AI clients
        // (Claude Desktop, VS Code) reuse the ID from a fresh TCP connection.
        context.SessionId.Should().BeNull(
            "the server must run in stateless mode so that no Mcp-Session-Id is issued " +
            "and no client request can be rejected with 404 due to a pending-session state");
    }

    [Fact]
    public async Task FreshConnection_ShouldWorkWithoutSessionIdAsync() {
        // Arrange - initialize on connection A
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act - brand-new HttpClient (connection B), no session header at all.
        // In stateless mode, every connection is independent; there is nothing
        // to "reuse" and the server must never reject a connection-less request.
        JsonDocument toolsResponse = await context.ToolsListWithFreshConnectionAsync(sessionId: null);

        // Assert
        JsonElement toolsResult = McpJsonRpcAssertions.GetJsonRpcResult(toolsResponse);
        toolsResult.TryGetProperty("tools", out JsonElement toolsArray).Should().BeTrue();
        toolsArray.ValueKind.Should().Be(JsonValueKind.Array);
        toolsArray.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StepOver_ShouldInitiateStepOverAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Act
        JsonDocument result = await context.CallToolAsync("step_over", new Dictionary<string, object?> { ["nextAddress"] = 0u, ["isCallOrInterrupt"] = false });

        // Assert
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(McpJsonRpcAssertions.GetStructuredContent(McpJsonRpcAssertions.GetJsonRpcResult(result)), "success", out JsonElement success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        AssertSuccessfulToolResponseContainsCpuStatus(result);
    }

    [Fact]
    public void DiscoverTools_ShouldMatchServiceToolNames() {
        // Arrange
        Spice86Creator creator = new(TestProgramName, false);
        using Spice86DependencyInjection spice86 = creator.Create();
        EmulatorMcpServices services = spice86.McpServices;

        // Act
        string[] serviceTools = services.GetAllToolNames().OrderBy(x => x).ToArray();
        string[] discoveredTools = EmulatorMcpServices.DiscoverToolNames().OrderBy(x => x).ToArray();

        // Assert
        serviceTools.Should().BeEquivalentTo(discoveredTools);
    }

    [Fact]
    public async Task ReadCfgCpuGraph_ShouldExposeSegmentedAddressesAndCorrectEntryPointCountAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Step to build up graph nodes
        for (int i = 0; i < 5; i++) {
            await context.CallToolAsync("step", new Dictionary<string, object?>());
        }

        // Act
        JsonDocument response = await context.CallToolAsync("read_cfg_cpu_graph", new Dictionary<string, object?> {
            ["nodeLimit"] = null
        });

        // Assert
        JsonElement structured = GetSuccessfulStructuredContent(response);

        // CurrentContextEntryPoint should be a SegmentedAddress object, not a string
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "currentContextEntryPoint", out JsonElement entryPoint).Should().BeTrue();
        entryPoint.ValueKind.Should().Be(JsonValueKind.Object);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(entryPoint, "segment", out JsonElement _).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(entryPoint, "offset", out JsonElement _).Should().BeTrue();

        // EntryPointAddresses should be an array of SegmentedAddress objects
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "entryPointAddresses", out JsonElement entryPoints).Should().BeTrue();
        entryPoints.ValueKind.Should().Be(JsonValueKind.Array);
        if (entryPoints.GetArrayLength() > 0) {
            JsonElement firstEntry = entryPoints[0];
            firstEntry.ValueKind.Should().Be(JsonValueKind.Object);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(firstEntry, "segment", out JsonElement _).Should().BeTrue();
        }

        // TotalEntryPoints should match the array length (distinct addresses, not instruction variants)
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "totalEntryPoints", out JsonElement total).Should().BeTrue();
        total.GetInt32().Should().Be(entryPoints.GetArrayLength());

        // LastExecutedAddress should be null or a SegmentedAddress object, never a "None" string
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "lastExecutedAddress", out JsonElement lastExec).Should().BeTrue();
        if (lastExec.ValueKind != JsonValueKind.Null) {
            lastExec.ValueKind.Should().Be(JsonValueKind.Object);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(lastExec, "segment", out JsonElement _).Should().BeTrue();
        }

        // Nodes array must be present and contain graph nodes
        AssertGraphNodesPresent(structured);
        AssertSuccessfulToolResponseContainsCpuStatus(response);
    }

    [Fact]
    public async Task ReadCfgCpuGraph_WithNodeLimit_ShouldRespectLimitAsync() {
        // Arrange
        await using McpIntegrationContext context = await McpIntegrationContext.CreateAsync(TestProgramName);
        await context.InitializeAsync();

        // Step a few times to build up graph nodes
        for (int i = 0; i < 5; i++) {
            await context.CallToolAsync("step", new Dictionary<string, object?>());
        }

        // First get full graph to know its size
        JsonDocument fullResponse = await context.CallToolAsync("read_cfg_cpu_graph", new Dictionary<string, object?> {
            ["nodeLimit"] = null
        });
        JsonElement fullStructured = GetSuccessfulStructuredContent(fullResponse);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(fullStructured, "nodes", out JsonElement fullNodes).Should().BeTrue();
        int fullCount = fullNodes.GetArrayLength();
        fullCount.Should().BeGreaterThan(0, "stepping should produce graph nodes");

        // Act — request with a limit smaller than the full graph
        int requestedLimit = 2;
        JsonDocument limitedResponse = await context.CallToolAsync("read_cfg_cpu_graph", new Dictionary<string, object?> {
            ["nodeLimit"] = requestedLimit
        });

        // Assert
        JsonElement limitedStructured = GetSuccessfulStructuredContent(limitedResponse);
        AssertGraphNodesPresent(limitedStructured);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(limitedStructured, "nodes", out JsonElement limitedNodes).Should().BeTrue();
        limitedNodes.GetArrayLength().Should().BeLessThanOrEqualTo(requestedLimit);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(limitedStructured, "truncated", out JsonElement truncated).Should().BeTrue();
        if (fullCount > requestedLimit) {
            truncated.GetBoolean().Should().BeTrue();
        }
        AssertSuccessfulToolResponseContainsCpuStatus(limitedResponse);
    }

    private static void AssertGraphNodesPresent(JsonElement structured) {
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structured, "nodes", out JsonElement nodes).Should().BeTrue();
        nodes.ValueKind.Should().Be(JsonValueKind.Array);
        if (nodes.GetArrayLength() > 0) {
            JsonElement node = nodes[0];
            node.ValueKind.Should().Be(JsonValueKind.Object);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(node, "id", out JsonElement _).Should().BeTrue();
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(node, "address", out JsonElement addr).Should().BeTrue();
            addr.ValueKind.Should().Be(JsonValueKind.Object);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(addr, "segment", out JsonElement _).Should().BeTrue();
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(node, "successorIds", out JsonElement succs).Should().BeTrue();
            succs.ValueKind.Should().Be(JsonValueKind.Array);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(node, "predecessorIds", out JsonElement preds).Should().BeTrue();
            preds.ValueKind.Should().Be(JsonValueKind.Array);
            McpJsonRpcAssertions.TryGetPropertyIgnoreCase(node, "isLive", out JsonElement _).Should().BeTrue();
        }
    }

    private static void AssertSuccessfulToolResponseContainsCpuStatus(JsonDocument response) {
        JsonElement result = McpJsonRpcAssertions.GetJsonRpcResult(response);
        result.TryGetProperty("isError", out JsonElement isError).Should().BeFalse();

        JsonElement structuredContent = McpJsonRpcAssertions.GetStructuredContent(result);
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(structuredContent, "cpuStatus", out JsonElement cpuStatus).Should().BeTrue();
        McpJsonRpcAssertions.TryGetPropertyIgnoreCase(cpuStatus, "cycles", out JsonElement cycles).Should().BeTrue();
        cycles.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.String);
    }
}
