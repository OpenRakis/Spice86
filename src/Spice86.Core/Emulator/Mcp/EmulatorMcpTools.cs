namespace Spice86.Mcp;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using SkiaSharp;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.Mcp.Response;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

[McpServerToolType]
internal sealed class EmulatorMcpTools {
    private static readonly string ScreenshotDirectory = Path.Join(Path.GetTempPath(), "spice86-mcp-screenshots");
    private static readonly TimeSpan StepCompletionTimeout = TimeSpan.FromSeconds(2);
    private readonly EmulatorMcpServices _services;

    public EmulatorMcpTools(EmulatorMcpServices services) => _services = services;

    private CallToolResult Success(McpToolResponse response) {
        JsonNode? responseNode = JsonSerializer.SerializeToNode(response, response.GetType());
        JsonObject structuredContent;
        if (responseNode is JsonObject responseObject) {
            structuredContent = responseObject;
        } else {
            structuredContent = new JsonObject {
                ["value"] = responseNode
            };
        }

        structuredContent["cpuStatus"] = JsonSerializer.SerializeToNode(BuildCpuStatus(_services.State));
        return new CallToolResult {
            StructuredContent = structuredContent
        };
    }

    private CallToolResult Error(string message) {
        JsonObject structuredContent = new JsonObject {
            ["success"] = false,
            ["message"] = message,
            ["cpuStatus"] = JsonSerializer.SerializeToNode(BuildCpuStatus(_services.State))
        };

        return new CallToolResult {
            IsError = true,
            StructuredContent = structuredContent,
            Content = [
                new TextContentBlock {
                    Text = message
                }
            ]
        };
    }

    private CallToolResult ExecuteTool(Func<McpToolResponse> action, [CallerMemberName] string methodName = "") {
        MethodInfo? method = typeof(EmulatorMcpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null) {
            return Error($"Unknown MCP tool method: {methodName}");
        }
        McpServerToolAttribute? toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
        if (toolAttr == null) {
            return Error($"Method '{methodName}' is missing [McpServerTool] attribute");
        }
        string toolName = string.IsNullOrWhiteSpace(toolAttr.Name) ? methodName : toolAttr.Name;
        bool autoPause = method.GetCustomAttribute<McpManualControlAttribute>() == null;

        bool shouldResumeOnCommandEnd = false;
        try {
            EnsureToolEnabled(toolName);
            if (autoPause && !_services.PauseHandler.IsPaused) {
                _services.PauseHandler.RequestPause($"to process MCP tool '{toolName}'");
                if (!WaitUntilPaused(StepCompletionTimeout)) {
                    throw new InvalidOperationException($"Timed out waiting to pause before running tool '{toolName}'");
                }
                shouldResumeOnCommandEnd = true;
            }

            return Success(action());
        } catch (ArgumentException ex) {
            return Error(ex.Message);
        } catch (InvalidOperationException ex) {
            return Error(ex.Message);
        } catch (KeyNotFoundException ex) {
            return Error(ex.Message);
        } catch (FormatException ex) {
            return Error(ex.Message);
        } catch (OverflowException ex) {
            return Error(ex.Message);
        } finally {
            if (shouldResumeOnCommandEnd) {
                _services.PauseHandler.Resume();
            }
        }
    }

    private void EnsureToolEnabled(string toolName) {
        if (!_services.IsToolEnabled(toolName)) {
            throw new InvalidOperationException($"Tool '{toolName}' is disabled.");
        }
    }

    private static McpSegmentedAddress ToMcpSegmentedAddress(uint physicalAddress) {
        ushort segment = MemoryUtils.ToSegment(physicalAddress);
        ushort offset = (ushort)(physicalAddress & 0xF);
        return new McpSegmentedAddress {
            Segment = segment,
            Offset = offset
        };
    }

    private static int ToPageSegment(int physicalPage) {
        int paragraphsPerPage = ExpandedMemoryManager.EmmPageSize / 16;
        return ExpandedMemoryManager.EmmPageFrameSegment + physicalPage * paragraphsPerPage;
    }

    private (bool IsMapped, int? HandleId, int? LogicalPage) ResolveEmsMappingForPhysicalPage(ushort physicalPage) {
        ExpandedMemoryManager? emsManager = _services.EmsManager;
        if (emsManager == null) {
            return (false, null, null);
        }

        EmmPage mappedPage = emsManager.EmmPageFrame[physicalPage].PhysicalPage;
        foreach (KeyValuePair<int, EmmHandle> handleEntry in emsManager.EmmHandles) {
            for (int logicalPage = 0; logicalPage < handleEntry.Value.LogicalPages.Count; logicalPage++) {
                EmmPage candidatePage = handleEntry.Value.LogicalPages[logicalPage];
                if (ReferenceEquals(candidatePage, mappedPage)) {
                    return (true, handleEntry.Key, logicalPage);
                }
            }
        }

        return (false, null, null);
    }

    [McpServerTool(Name = "read_cpu_registers", UseStructuredContent = true), Description("Read CPU registers")]
    public CallToolResult ReadCpuRegisters() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                return BuildCpuRegistersResponse(_services.State);
            }
        });
    }

    [McpServerTool(Name = "read_memory", UseStructuredContent = true), Description("Read memory range (max 4096 bytes) from segmented address (segment:offset). Returns hex-encoded data.")]
    public CallToolResult ReadMemory(ushort segment, ushort offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }

                uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
                byte[] data = _services.Memory.ReadRam((uint)length, address);
                return new MemoryReadResponse {
                    Address = new McpSegmentedAddress {
                        Segment = segment,
                        Offset = offset
                    },
                    Length = length,
                    Data = Convert.ToHexString(data)
                };
            }
        });
    }

    [McpServerTool(Name = "write_memory", UseStructuredContent = true), Description("Write hex-encoded bytes to segmented memory address (segment:offset). Maximum 4096 bytes.")]
    public CallToolResult WriteMemory(ushort segment, ushort offset, [StringSyntax("Hexadecimal")] string data) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                byte[] bytesToWrite = ParseHexData(data);
                if (bytesToWrite.Length > 4096) {
                    throw new InvalidOperationException("Data length must be between 1 and 4096 bytes");
                }

                uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
                ValidateMemoryWriteRange(address, bytesToWrite.Length);
                _services.Memory.WriteRam(bytesToWrite, address);
                return new MemoryWriteResponse {
                    Address = new McpSegmentedAddress {
                        Segment = segment,
                        Offset = offset
                    },
                    Length = bytesToWrite.Length,
                    Success = true
                };
            }
        });
    }

    [McpServerTool(Name = "search_memory", UseStructuredContent = true), Description("Search RAM for a hex-encoded byte sequence from segmented address (segment:offset). Returns segmented addresses of matches.")]
    public CallToolResult SearchMemory([StringSyntax("Hexadecimal")] string pattern, ushort startSegment, ushort startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);

                uint startAddress = MemoryUtils.ToPhysicalAddress(startSegment, startOffset);
                int searchLength = ComputeSearchLength(startAddress, length, _services.Memory.Length);
                if (searchLength <= 0) {
                    return EmptyMemorySearchResponse(needle, startSegment, startOffset);
                }

                (uint[] matches, bool truncated) = SearchRamMatches(startAddress, searchLength, needle, limit);
                return new MemorySearchResponse {
                    Pattern = Convert.ToHexString(needle),
                    StartAddress = new McpSegmentedAddress { Segment = startSegment, Offset = startOffset },
                    Length = searchLength,
                    Matches = matches.Select(ToMcpSegmentedAddress).ToArray(),
                    Truncated = truncated
                };
            }
        });
    }

    private static byte[] ParseHexPattern([StringSyntax("Hexadecimal")] string pattern) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            throw new ArgumentException("Pattern must not be empty", nameof(pattern));
        }

        string normalized = new string(pattern.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[2..];
        }
        if ((normalized.Length & 1) != 0) {
            throw new ArgumentException("Pattern hex string length must be even", nameof(pattern));
        }

        try {
            byte[] needle = Convert.FromHexString(normalized);
            if (needle.Length == 0) {
                throw new ArgumentException("Pattern must contain at least one byte", nameof(pattern));
            }
            return needle;
        } catch (FormatException ex) {
            throw new ArgumentException("Pattern must be a valid hex string", nameof(pattern), ex);
        }
    }

    private static byte[] ParseHexData([StringSyntax("Hexadecimal")] string data) {
        if (string.IsNullOrWhiteSpace(data)) {
            throw new ArgumentException("Data must not be empty", nameof(data));
        }

        string normalized = new string(data.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[2..];
        }
        if ((normalized.Length & 1) != 0) {
            throw new ArgumentException("Data hex string length must be even", nameof(data));
        }

        try {
            byte[] bytes = Convert.FromHexString(normalized);
            if (bytes.Length == 0) {
                throw new ArgumentException("Data must contain at least one byte", nameof(data));
            }
            return bytes;
        } catch (FormatException ex) {
            throw new ArgumentException("Data must be a valid hex string", nameof(data), ex);
        }
    }

    private void ValidateMemoryWriteRange(uint address, int length) {
        if (address >= _services.Memory.Length) {
            throw new ArgumentOutOfRangeException(nameof(address), "Start address is outside memory range");
        }

        int maxLength = _services.Memory.Length - (int)address;
        if (length > maxLength) {
            throw new InvalidOperationException("Data exceeds memory bounds");
        }
    }

    private static void ValidateLimit(int limit) {
        if (limit <= 0 || limit > 10_000) {
            throw new ArgumentException("Limit must be between 1 and 10000", nameof(limit));
        }
    }

    private static int ComputeSearchLength(uint startAddress, int requestedLength, int memoryLength) {
        if (startAddress >= memoryLength) {
            throw new ArgumentOutOfRangeException(nameof(startAddress), "Start address is outside memory range");
        }

        int maxLength = memoryLength - (int)startAddress;
        if (requestedLength <= 0) {
            return maxLength;
        }
        return Math.Min(requestedLength, maxLength);
    }

    private static MemorySearchResponse EmptyMemorySearchResponse(byte[] needle, ushort startSegment, ushort startOffset) {
        return new MemorySearchResponse {
            Pattern = Convert.ToHexString(needle),
            StartAddress = new McpSegmentedAddress { Segment = startSegment, Offset = startOffset },
            Length = 0,
            Matches = Array.Empty<McpSegmentedAddress>(),
            Truncated = false
        };
    }

    private (uint[] Matches, bool Truncated) SearchRamMatches(uint startAddress, int searchLength, byte[] needle, int limit) {
        List<uint> matches = new();
        uint current = startAddress;
        int remaining = searchLength;

        while (remaining > 0 && matches.Count < limit) {
            uint? found = _services.Memory.SearchValue(current, remaining, needle);
            if (!found.HasValue) {
                break;
            }

            uint next = found.Value + 1;
            matches.Add(found.Value);
            if (next <= found.Value || found.Value < current) {
                break;
            }
            remaining = Math.Max(0, (int)((long)startAddress + searchLength - next));
            current = next;
        }

        bool truncated = matches.Count >= limit && remaining > 0;
        return (matches.ToArray(), truncated);
    }

    [McpServerTool(Name = "list_functions", UseStructuredContent = true), Description("List functions ordered by call count")]
    public CallToolResult ListFunctions(int? limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                int effectiveLimit = limit ?? 100;
                if (effectiveLimit <= 0) {
                    effectiveLimit = 100;
                }

                FunctionInfo[] functions = _services.FunctionCatalogue.FunctionInformations.Values
                .OrderByDescending(f => f.CalledCount)
                .Take(effectiveLimit)
                .Select(f => new FunctionInfo {
                    Address = f.Address.ToString(),
                    Name = f.Name,
                    CalledCount = f.CalledCount,
                    HasOverride = f.HasOverride
                })
                .ToArray();

                return new FunctionListResponse {
                    Functions = functions,
                    TotalCount = _services.FunctionCatalogue.FunctionInformations.Count
                };
            }
        });
    }

    [McpServerTool(Name = "read_cfg_cpu_graph", UseStructuredContent = true), Description("Read CFG CPU statistics")]
    public CallToolResult ReadCfgCpuGraph() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                ExecutionContextManager contextManager = _services.CfgCpu.ExecutionContextManager;
                ExecutionContext currentContext = contextManager.CurrentExecutionContext;

                int totalEntryPoints = contextManager.ExecutionContextEntryPoints
                    .Sum(kvp => kvp.Value.Count);

                string[] entryPointAddresses = contextManager.ExecutionContextEntryPoints
                    .Select(kvp => kvp.Key.ToString())
                    .ToArray();

                return new CfgCpuGraphResponse {
                    CurrentContextDepth = currentContext.Depth,
                    CurrentContextEntryPoint = currentContext.EntryPoint.ToString(),
                    TotalEntryPoints = totalEntryPoints,
                    EntryPointAddresses = entryPointAddresses,
                    LastExecutedAddress = currentContext.LastExecuted?.Address.ToString() ?? "None"
                };
            }
        });
    }

    [McpServerTool(Name = "read_io_port", UseStructuredContent = true), Description("Read from IO port")]
    public CallToolResult ReadIoPort(int port) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (port < 0 || port > 65535) {
                    throw new ArgumentException("Port must be 0-65535");
                }
                byte value = _services.IoPortDispatcher.ReadByte((ushort)port);
                return new IoPortReadResponse { Port = port, Value = value };
            }
        });
    }

    [McpServerTool(Name = "write_io_port", UseStructuredContent = true), Description("Write to IO port")]
    public CallToolResult WriteIoPort(int port, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (port < 0 || port > 65535) {
                    throw new ArgumentException("Port must be 0-65535");
                }
                if (value < 0 || value > 255) {
                    throw new ArgumentException("Value must be 0-255");
                }
                _services.IoPortDispatcher.WriteByte((ushort)port, (byte)value);
                return new IoPortWriteResponse { Port = port, Value = value, Success = true };
            }
        });
    }

    [McpServerTool(Name = "send_keyboard_key", UseStructuredContent = true), Description("Send a keyboard key press/release through the PS/2 controller. Key must be a PcKeyboardKey name, for example Escape, Enter, A, Up.")]
    public CallToolResult SendKeyboardKey(string key, bool isPressed) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Intel8042Controller? controller = _services.Intel8042Controller;
                if (controller == null) {
                    throw new InvalidOperationException("PS/2 controller is not available");
                }

                if (!Enum.TryParse(key, true, out PcKeyboardKey parsedKey)) {
                    throw new ArgumentException($"Invalid PcKeyboardKey: '{key}'");
                }

                controller.KeyboardDevice.EnqueueKeyEvent(parsedKey, isPressed);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Keyboard event sent: {parsedKey} {(isPressed ? "down" : "up")}"
                };
            }
        });
    }

    [McpServerTool(Name = "send_mouse_packet", UseStructuredContent = true), Description("Send raw PS/2 mouse packet bytes (hex) through the controller AUX port. Example: 080000 (no movement, no button).")]
    public CallToolResult SendMousePacket([StringSyntax("Hexadecimal")] string packetData) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Intel8042Controller? controller = _services.Intel8042Controller;
                if (controller == null) {
                    throw new InvalidOperationException("PS/2 controller is not available");
                }

                byte[] bytes = ParseHexData(packetData);
                if (bytes.Length > 8) {
                    throw new ArgumentException("Mouse packet must be 1-8 bytes", nameof(packetData));
                }

                foreach (byte packetByte in bytes) {
                    controller.AddAuxByte(packetByte);
                }

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Mouse packet sent ({bytes.Length} byte(s))"
                };
            }
        });
    }

    [McpServerTool(Name = "get_video_state", UseStructuredContent = true), Description("Get video card state")]
    public CallToolResult GetVideoState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                return new VideoStateResponse {
                    Width = _services.VgaRenderer.Width,
                    Height = _services.VgaRenderer.Height,
                    BufferSize = _services.VgaRenderer.BufferSize
                };
            }
        });
    }

    [McpServerTool(Name = "query_sound_blaster_state", UseStructuredContent = true), Description("Query Sound Blaster configuration and DSP state")]
    public CallToolResult QuerySoundBlasterState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                return new SoundBlasterStateResponse {
                    SbType = soundBlaster.SbTypeProperty.ToString(),
                    BaseAddress = soundBlaster.BaseAddress,
                    Irq = soundBlaster.IRQ,
                    LowDma = soundBlaster.LowDma,
                    HighDma = soundBlaster.HighDma,
                    BlasterString = soundBlaster.BlasterString,
                    SpeakerEnabled = soundBlaster.IsSpeakerEnabled,
                    DspFrequencyHz = soundBlaster.DspFrequencyHz,
                    DspTestRegister = soundBlaster.DspTestRegister
                };
            }
        });
    }

    [McpServerTool(Name = "sound_blaster_set_speaker", UseStructuredContent = true), Description("Enable or disable the Sound Blaster DSP speaker using the device command interface")]
    public CallToolResult SoundBlasterSetSpeaker(bool enabled) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                ushort dspWritePort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspWriteData);
                byte command = enabled ? (byte)SoundBlaster.DspCommand.EnableSpeaker : (byte)SoundBlaster.DspCommand.DisableSpeaker;
                soundBlaster.WriteByte(dspWritePort, command);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = enabled ? "Sound Blaster speaker enabled" : "Sound Blaster speaker disabled"
                };
            }
        });
    }

    [McpServerTool(Name = "query_sound_blaster_dsp_version", UseStructuredContent = true), Description("Query the Sound Blaster DSP version using the DSP command interface")]
    public CallToolResult QuerySoundBlasterDspVersion() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                return ReadSoundBlasterDspVersion(soundBlaster);
            }
        });
    }

    [McpServerTool(Name = "query_sound_blaster_mixer_state", UseStructuredContent = true), Description("Query commonly used Sound Blaster mixer registers and stereo/filter state")]
    public CallToolResult QuerySoundBlasterMixerState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                byte outputControl = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.OutputStereoSelect);
                return new SoundBlasterMixerStateResponse {
                    MasterLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MasterVolumeLeft),
                    MasterRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MasterVolumeRight),
                    DacLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.DacVolumeLeftOrMasterEss),
                    DacRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.DacVolumeRight),
                    FmLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.FmVolumeLeft),
                    FmRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.FmVolumeRight),
                    CdLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.CdAudioVolumeLeftOrFmEss),
                    CdRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.CdAudioVolumeRight),
                    LineInLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.LineInVolumeLeftOrCdEss),
                    LineInRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.LineInVolumeRight),
                    Microphone = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MicVolume),
                    StereoOutputEnabled = (outputControl & 0x02) != 0,
                    LowPassFilterEnabled = (outputControl & 0x20) == 0
                };
            }
        });
    }

    [McpServerTool(Name = "sound_blaster_write_mixer_register", UseStructuredContent = true), Description("Write a Sound Blaster mixer register using semantic mixer access instead of raw port writes")]
    public CallToolResult SoundBlasterWriteMixerRegister(int register, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                if (register < 0 || register > 0xFF) {
                    throw new ArgumentException("Register must be between 0x00 and 0xFF");
                }
                if (value < 0 || value > 0xFF) {
                    throw new ArgumentException("Value must be between 0x00 and 0xFF");
                }

                WriteSoundBlasterMixerRegister(soundBlaster, (byte)register, (byte)value);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote Sound Blaster mixer register 0x{register:X2} = 0x{value:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "query_opl_state", UseStructuredContent = true), Description("Query OPL/AdLib synthesis mode and mixer channel state")]
    public CallToolResult QueryOplState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Opl3Fm opl3Fm = GetOpl3Fm();
                SoundChannel mixerChannel = opl3Fm.MixerChannel;
                return new OplStateResponse {
                    Mode = opl3Fm.Mode.ToString(),
                    AdlibGoldEnabled = opl3Fm.IsAdlibGoldEnabled,
                    MixerChannelName = mixerChannel.Name,
                    MixerChannelSampleRate = mixerChannel.SampleRate,
                    MixerChannelEnabled = mixerChannel.IsEnabled
                };
            }
        });
    }

    [McpServerTool(Name = "query_pc_speaker_state", UseStructuredContent = true), Description("Query PC speaker control port state and mixer channel information")]
    public CallToolResult QueryPcSpeakerState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                PcSpeaker pcSpeaker = GetPcSpeaker();
                SoundChannel mixerChannel = pcSpeaker.Channel;
                return new PcSpeakerStateResponse {
                    ControlPort = 0x61,
                    ControlValue = pcSpeaker.ControlPortValue,
                    Timer2GateEnabled = pcSpeaker.IsTimer2GateEnabled,
                    SpeakerOutputEnabled = pcSpeaker.IsSpeakerOutputEnabled,
                    Timer2OutputHigh = pcSpeaker.IsTimer2OutputHigh,
                    MixerChannelName = mixerChannel.Name,
                    MixerChannelSampleRate = mixerChannel.SampleRate,
                    MixerChannelEnabled = mixerChannel.IsEnabled
                };
            }
        });
    }

    [McpServerTool(Name = "pc_speaker_set_control", UseStructuredContent = true), Description("Set PC speaker timer gate and speaker output bits through port 0x61 semantics")]
    public CallToolResult PcSpeakerSetControl(bool timer2GateEnabled, bool speakerOutputEnabled) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                PcSpeaker pcSpeaker = GetPcSpeaker();
                PpiPortB portState = new() {
                    Data = pcSpeaker.ControlPortValue
                };
                portState.Timer2Gating = timer2GateEnabled;
                portState.SpeakerOutput = speakerOutputEnabled;
                pcSpeaker.WriteByte(0x61, portState.Data);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"PC speaker control updated: gate={(timer2GateEnabled ? 1 : 0)}, output={(speakerOutputEnabled ? 1 : 0)}"
                };
            }
        });
    }

    [McpServerTool(Name = "query_midi_state", UseStructuredContent = true), Description("Query MPU-401 MIDI mode, backend selection, and status port flags")]
    public CallToolResult QueryMidiState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                GeneralMidiStatus status = midi.Status;
                return new MidiStateResponse {
                    DeviceKind = midi.UseMT32 ? "MT32" : "GeneralMidi",
                    UseMt32 = midi.UseMT32,
                    Mt32RomsPath = midi.Mt32RomsPath,
                    State = midi.State.ToString(),
                    StatusValue = (byte)status,
                    InputReady = (status & GeneralMidiStatus.InputReady) != 0,
                    OutputReady = (status & GeneralMidiStatus.OutputReady) != 0,
                    DataPort = Midi.DataPort,
                    StatusPort = Midi.StatusPort
                };
            }
        });
    }

    [McpServerTool(Name = "midi_reset", UseStructuredContent = true), Description("Reset the MPU-401 MIDI interface and enqueue an acknowledge byte")]
    public CallToolResult MidiReset() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                midi.WriteByte(Midi.StatusPort, Midi.ResetCommand);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = "MIDI interface reset"
                };
            }
        });
    }

    [McpServerTool(Name = "midi_enter_uart_mode", UseStructuredContent = true), Description("Put the MPU-401 MIDI interface into UART mode")]
    public CallToolResult MidiEnterUartMode() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                midi.WriteByte(Midi.StatusPort, Midi.EnterUartModeCommand);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = "MIDI interface entered UART mode"
                };
            }
        });
    }

    [McpServerTool(Name = "midi_send_bytes", UseStructuredContent = true), Description("Send raw MIDI bytes as hex through the MPU-401 data port. Supports short messages and SysEx streams.")]
    public CallToolResult MidiSendBytes([StringSyntax("Hexadecimal")] string data) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                byte[] bytes = ParseHexData(data);
                if (bytes.Length == 0 || bytes.Length > 1024) {
                    throw new ArgumentException("MIDI data length must be between 1 and 1024 bytes", nameof(data));
                }

                foreach (byte value in bytes) {
                    midi.WriteByte(Midi.DataPort, value);
                }

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Sent {bytes.Length} MIDI byte(s)"
                };
            }
        });
    }

    [McpServerTool(Name = "opl_write_register", UseStructuredContent = true), Description("Write an OPL register using register/value semantics instead of raw port writes")]
    public CallToolResult OplWriteRegister(int register, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Opl3Fm opl3Fm = GetOpl3Fm();
                if (register < 0 || register > 0x1FF) {
                    throw new ArgumentException("Register must be between 0x000 and 0x1FF");
                }
                if (value < 0 || value > 0xFF) {
                    throw new ArgumentException("Value must be between 0x00 and 0xFF");
                }
                if (opl3Fm.Mode == OplMode.Opl2 && register > 0xFF) {
                    throw new ArgumentException("OPL2 mode only supports registers between 0x00 and 0xFF");
                }

                ushort addressPort = register <= 0xFF ? (ushort)0x388 : (ushort)0x38A;
                ushort dataPort = (ushort)(addressPort + 1);
                opl3Fm.WriteByte(addressPort, (byte)(register & 0xFF));
                opl3Fm.WriteByte(dataPort, (byte)value);

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote OPL register 0x{register:X3} = 0x{value:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "query_video_state_detailed", UseStructuredContent = true), Description("Query VGA mode, BIOS video state, and cursor position using semantic video APIs")]
    public CallToolResult QueryVideoStateDetailed() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                CursorPosition cursorPosition = vgaFunctionality.GetCursorPosition(biosDataArea.CurrentVideoPage);
                return new VideoDetailedStateResponse {
                    BiosVideoMode = biosDataArea.VideoMode,
                    MemoryModel = currentMode.MemoryModel.ToString(),
                    Width = currentMode.Width,
                    Height = currentMode.Height,
                    BitsPerPixel = currentMode.BitsPerPixel,
                    CharacterWidth = currentMode.CharacterWidth,
                    CharacterHeight = currentMode.CharacterHeight,
                    StartSegment = currentMode.StartSegment,
                    ScreenColumns = biosDataArea.ScreenColumns,
                    ScreenRows = biosDataArea.ScreenRows,
                    ActivePage = biosDataArea.CurrentVideoPage,
                    CursorX = cursorPosition.X,
                    CursorY = cursorPosition.Y,
                    RendererWidth = _services.VgaRenderer.Width,
                    RendererHeight = _services.VgaRenderer.Height,
                    BufferSize = _services.VgaRenderer.BufferSize
                };
            }
        });
    }

    [McpServerTool(Name = "video_set_mode", UseStructuredContent = true), Description("Set a VGA video mode using the high-level video subsystem")]
    public CallToolResult VideoSetMode(int modeId, bool clearVideoMemory) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ModeFlags modeFlags = clearVideoMemory ? 0 : ModeFlags.NoClearMem;
                vgaFunctionality.VgaSetMode(modeId, modeFlags);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Video mode set to 0x{modeId:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_write_text", UseStructuredContent = true), Description("Write a text string using the VGA text output abstraction")]
    public CallToolResult VideoWriteText(string text) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                vgaFunctionality.WriteString(text);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote {text.Length} character(s) to the video device"
                };
            }
        });
    }

    [McpServerTool(Name = "query_video_cursor", UseStructuredContent = true), Description("Query the cursor position for a text page and the character currently stored at that location")]
    public CallToolResult QueryVideoCursor(int page) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ValidateVideoPage(page);

                CursorPosition cursorPosition = vgaFunctionality.GetCursorPosition((byte)page);
                CharacterPlusAttribute character = vgaFunctionality.ReadChar(cursorPosition);
                return BuildVideoCursorResponse(cursorPosition, character);
            }
        });
    }

    [McpServerTool(Name = "video_set_cursor_position", UseStructuredContent = true), Description("Set the cursor position on a text page")]
    public CallToolResult VideoSetCursorPosition(int page, int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                ValidateTextCoordinates(page, x, y, biosDataArea);

                vgaFunctionality.SetCursorPosition(new CursorPosition(x, y, page));
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Cursor moved to ({x}, {y}) on page {page}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_read_character", UseStructuredContent = true), Description("Read the character and attribute stored at a text position")]
    public CallToolResult VideoReadCharacter(int page, int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                ValidateTextCoordinates(page, x, y, biosDataArea);

                CursorPosition cursorPosition = new(x, y, page);
                CharacterPlusAttribute character = vgaFunctionality.ReadChar(cursorPosition);
                return new VideoCharacterResponse {
                    Page = page,
                    X = x,
                    Y = y,
                    Character = character.Character.ToString(),
                    Attribute = character.Attribute,
                    UseAttribute = character.UseAttribute
                };
            }
        });
    }

    [McpServerTool(Name = "video_set_active_page", UseStructuredContent = true), Description("Set the active VGA text page")]
    public CallToolResult VideoSetActivePage(int page) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ValidateVideoPage(page);

                int pageStartAddress = vgaFunctionality.SetActivePage((byte)page);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Active video page set to {page} (start 0x{pageStartAddress:X})"
                };
            }
        });
    }

    [McpServerTool(Name = "query_video_palette", UseStructuredContent = true), Description("Query the EGA/VGA palette registers, overscan color, and pixel mask")]
    public CallToolResult QueryVideoPalette() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                byte[] registers = vgaFunctionality.GetAllPaletteRegisters();
                return new VideoPaletteStateResponse {
                    Registers = registers.Select(static x => (int)x).ToArray(),
                    OverscanBorderColor = vgaFunctionality.GetOverscanBorderColor(),
                    PixelMask = vgaFunctionality.ReadPixelMask(),
                    ColorPageState = vgaFunctionality.ReadColorPageState()
                };
            }
        });
    }

    [McpServerTool(Name = "video_write_pixel", UseStructuredContent = true), Description("Write a pixel in the current graphics mode")]
    public CallToolResult VideoWritePixel(int x, int y, int color) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                ValidatePixelCoordinates(x, y, currentMode);
                if (color < 0 || color > 0xFF) {
                    throw new ArgumentException("Color must be between 0x00 and 0xFF");
                }

                vgaFunctionality.WritePixel((byte)color, (ushort)x, (ushort)y);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Pixel ({x}, {y}) set to 0x{color:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_read_pixel", UseStructuredContent = true), Description("Read a pixel from the current graphics mode")]
    public CallToolResult VideoReadPixel(int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                ValidatePixelCoordinates(x, y, currentMode);

                byte color = vgaFunctionality.ReadPixel((ushort)x, (ushort)y);
                return new VideoPixelResponse {
                    X = x,
                    Y = y,
                    Color = color
                };
            }
        });
    }

    [McpServerTool(Name = "query_bios_data_area", UseStructuredContent = true), Description("Read key BIOS Data Area fields relevant to machine and video state")]
    public CallToolResult QueryBiosDataArea() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                BiosDataArea biosDataArea = GetBiosDataArea();
                return new BiosDataAreaResponse {
                    ConventionalMemorySizeKb = biosDataArea.ConventionalMemorySizeKb,
                    EquipmentListFlags = biosDataArea.EquipmentListFlags,
                    VideoMode = biosDataArea.VideoMode,
                    ScreenColumns = biosDataArea.ScreenColumns,
                    ScreenRows = biosDataArea.ScreenRows,
                    CurrentVideoPage = biosDataArea.CurrentVideoPage,
                    CharacterHeight = biosDataArea.CharacterHeight,
                    CrtControllerBaseAddress = biosDataArea.CrtControllerBaseAddress,
                    TimerCounter = biosDataArea.TimerCounter,
                    LastUnexpectedIrq = biosDataArea.LastUnexpectedIrq
                };
            }
        });
    }

    [McpServerTool(Name = "query_interrupt_vector", UseStructuredContent = true), Description("Read an interrupt vector from the interrupt vector table using vector number semantics")]
    public CallToolResult QueryInterruptVector(int vectorNumber) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (vectorNumber < 0 || vectorNumber > 0xFF) {
                    throw new ArgumentException("Interrupt vector must be between 0x00 and 0xFF");
                }

                InterruptVectorTable interruptVectorTable = GetInterruptVectorTable();
                SegmentedAddress address = interruptVectorTable[vectorNumber];
                return new InterruptVectorResponse {
                    VectorNumber = vectorNumber,
                    Address = new McpSegmentedAddress {
                        Segment = address.Segment,
                        Offset = address.Offset
                    }
                };
            }
        });
    }

    [McpServerTool(Name = "query_dos_state", UseStructuredContent = true), Description("Query DOS drive selection, mounted drives, and DOS kernel state")]
    public CallToolResult QueryDosState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                List<DosDriveResponse> drives = dos.DosDriveManager.Values
                    .Select(static drive => new DosDriveResponse {
                        Drive = drive.DosVolume,
                        CurrentDosDirectory = drive.CurrentDosDirectory,
                        MountedHostDirectory = drive.MountedHostDirectory
                    })
                    .ToList();

                return new DosStateResponse {
                    CurrentDrive = dos.DosDriveManager.CurrentDrive.DosVolume,
                    CurrentDriveIndex = dos.DosDriveManager.CurrentDriveIndex,
                    PotentialDriveLetters = dos.DosDriveManager.NumberOfPotentiallyValidDriveLetters,
                    CurrentProgramSegmentPrefix = dos.DosSwappableDataArea.CurrentProgramSegmentPrefix,
                    DeviceCount = dos.Devices.Count,
                    HasEms = dos.Ems != null,
                    HasXms = dos.Xms != null,
                    Drives = drives
                };
            }
        });
    }

    [McpServerTool(Name = "query_dos_current_directory", UseStructuredContent = true), Description("Query the current DOS directory for the current drive or for an explicit drive letter")]
    public CallToolResult QueryDosCurrentDirectory(string driveLetter) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                byte driveNumber = ResolveDosDriveNumber(dos, driveLetter, out string resolvedDrive);
                DosFileOperationResult result = dos.FileManager.GetCurrentDir(driveNumber, out string currentDirectory);
                EnsureDosFileOperationSucceeded(result, $"Could not query current directory for {resolvedDrive}");

                return new DosCurrentDirectoryResponse {
                    Drive = resolvedDrive,
                    CurrentDirectory = currentDirectory
                };
            }
        });
    }

    [McpServerTool(Name = "dos_set_current_directory", UseStructuredContent = true), Description("Set the DOS current directory using DOS path semantics")]
    public CallToolResult DosSetCurrentDirectory(string path) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                DosFileOperationResult result = dos.FileManager.SetCurrentDir(path);
                EnsureDosFileOperationSucceeded(result, $"Could not set current directory to '{path}'");

                return new DosCurrentDirectoryResponse {
                    Drive = dos.DosDriveManager.CurrentDrive.DosVolume,
                    CurrentDirectory = dos.DosDriveManager.CurrentDrive.CurrentDosDirectory
                };
            }
        });
    }

    [McpServerTool(Name = "query_dos_program_state", UseStructuredContent = true), Description("Query the current DOS PSP and related process state")]
    public CallToolResult QueryDosProgramState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                ushort currentPspSegment = dos.DosSwappableDataArea.CurrentProgramSegmentPrefix;
                DosProgramSegmentPrefix currentPsp = new(_services.Memory, MemoryUtils.ToPhysicalAddress(currentPspSegment, 0));

                return new DosProgramStateResponse {
                    CurrentProgramSegmentPrefix = currentPspSegment,
                    ParentProgramSegmentPrefix = currentPsp.ParentProgramSegmentPrefix,
                    EnvironmentTableSegment = currentPsp.EnvironmentTableSegment,
                    MaximumOpenFiles = currentPsp.MaximumOpenFiles,
                    CurrentSizeParagraphs = currentPsp.CurrentSize,
                    CommandTailLength = currentPsp.DosCommandTail.Length
                };
            }
        });
    }

    [McpServerTool(Name = "dos_set_default_drive", UseStructuredContent = true), Description("Set the DOS default drive by letter without issuing raw INT 21h calls")]
    public CallToolResult DosSetDefaultDrive(string driveLetter) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length != 1) {
                    throw new ArgumentException("Drive letter must be a single character between A and Z");
                }

                char normalizedDriveLetter = char.ToUpperInvariant(driveLetter[0]);
                if (!dos.DosDriveManager.TryGetValue(normalizedDriveLetter, out VirtualDrive? mountedDrive) || mountedDrive == null) {
                    throw new InvalidOperationException($"Drive '{normalizedDriveLetter}' is not mounted");
                }

                dos.DosDriveManager.CurrentDrive = mountedDrive;
                dos.DosSwappableDataArea.CurrentDrive = dos.DosDriveManager.CurrentDriveIndex;
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"DOS default drive set to {mountedDrive.DosVolume}"
                };
            }
        });
    }

    [McpServerTool(Name = "screenshot", UseStructuredContent = true), Description("Capture screenshot as PNG image file and return path metadata")]
    public CallToolResult TakeScreenshot() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                int width = _services.VgaRenderer.Width;
                int height = _services.VgaRenderer.Height;
                uint[] buffer = new uint[width * height];
                _services.VgaRenderer.Render(buffer);

                byte[] bytes = new byte[buffer.Length * 4];
                Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);

                Directory.CreateDirectory(ScreenshotDirectory);
                string fileName = $"spice86-mcp-screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png";
                string filePath = Path.Join(ScreenshotDirectory, fileName);

                SKImageInfo imageInfo = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using SKBitmap bitmap = new(imageInfo);
                IntPtr pointer = bitmap.GetPixels();
                Marshal.Copy(bytes, 0, pointer, bytes.Length);

                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                if (pngData == null) {
                    throw new InvalidOperationException("Failed to encode screenshot as PNG.");
                }

                using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read)) {
                    pngData.SaveTo(fileStream);
                }

                FileInfo fileInfo = new(filePath);
                Uri fileUri = new(filePath);

                return new ScreenshotResponse {
                    Width = width,
                    Height = height,
                    Format = "png",
                    MimeType = "image/png",
                    FilePath = filePath,
                    FileUri = fileUri.AbsoluteUri,
                    FileSizeBytes = fileInfo.Length
                };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "pause_emulator", UseStructuredContent = true), Description("Immediately stop the emulation. Use this to inspect state at an arbitrary point.")]
    public CallToolResult PauseEmulator() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already paused" };
                }
                _services.PauseHandler.RequestPause("MCP server request");
                return new EmulatorControlResponse { Success = true, Message = "Paused" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "resume_emulator", UseStructuredContent = true), Description("Resume continuous execution of the emulator. Also known as 'go'.")]
    public CallToolResult ResumeEmulator() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already running" };
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Resumed" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "go", UseStructuredContent = true), Description("Alias for resume_emulator. Resumes continuous execution.")]
    public CallToolResult Go() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already running" };
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Resumed" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "step", UseStructuredContent = true), Description("Execute exactly one CPU instruction and then pause again. Useful for trace analysis.")]
    public CallToolResult Step() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    _services.PauseHandler.RequestPause("Step requested while running");
                    if (!WaitUntilPaused(StepCompletionTimeout)) {
                        throw new InvalidOperationException("Could not pause emulator before stepping.");
                    }
                }

                _services.CfgCpu.ExecuteNext();

                CpuRegistersResponse registers = BuildCpuRegistersResponse(_services.State);
                return new StepResponse {
                    Success = true,
                    Message = "Step completed",
                    GeneralPurpose = registers.GeneralPurpose,
                    Segments = registers.Segments,
                    InstructionPointer = registers.InstructionPointer,
                    Flags = registers.Flags
                };
            }
        });
    }

    private static CpuRegistersResponse BuildCpuRegistersResponse(State state) {
        return new CpuRegistersResponse {
            GeneralPurpose = new GeneralPurposeRegisters {
                EAX = state.EAX, EBX = state.EBX, ECX = state.ECX, EDX = state.EDX,
                ESI = state.ESI, EDI = state.EDI, ESP = state.ESP, EBP = state.EBP
            },
            Segments = new SegmentRegisters {
                CS = state.CS, DS = state.DS, ES = state.ES,
                FS = state.FS, GS = state.GS, SS = state.SS
            },
            InstructionPointer = new InstructionPointer { IP = state.IP },
            Flags = new CpuFlags {
                CarryFlag = state.CarryFlag, ParityFlag = state.ParityFlag,
                AuxiliaryFlag = state.AuxiliaryFlag, ZeroFlag = state.ZeroFlag,
                SignFlag = state.SignFlag, DirectionFlag = state.DirectionFlag,
                OverflowFlag = state.OverflowFlag, InterruptFlag = state.InterruptFlag
            }
        };
    }

    private SoundBlaster GetSoundBlaster() {
        return _services.SoundBlaster ?? throw new InvalidOperationException("Sound Blaster is not available");
    }

    private Opl3Fm GetOpl3Fm() {
        return _services.Opl3Fm ?? throw new InvalidOperationException("OPL is not available");
    }

    private PcSpeaker GetPcSpeaker() {
        return _services.PcSpeaker ?? throw new InvalidOperationException("PC speaker is not available");
    }

    private Midi GetMidi() {
        return _services.Midi ?? throw new InvalidOperationException("MIDI device is not available");
    }

    private IVgaFunctionality GetVgaFunctionality() {
        return _services.VgaFunctionality ?? throw new InvalidOperationException("VGA functionality is not available");
    }

    private BiosDataArea GetBiosDataArea() {
        return _services.BiosDataArea ?? throw new InvalidOperationException("BIOS data area is not available");
    }

    private InterruptVectorTable GetInterruptVectorTable() {
        return _services.InterruptVectorTable ?? throw new InvalidOperationException("Interrupt vector table is not available");
    }

    private Dos GetDos() {
        return _services.Dos ?? throw new InvalidOperationException("DOS is not available");
    }

    private static SoundBlasterDspVersionResponse ReadSoundBlasterDspVersion(SoundBlaster soundBlaster) {
        if (soundBlaster.SbTypeProperty == SbType.None) {
            return new SoundBlasterDspVersionResponse {
                MajorVersion = 0,
                MinorVersion = 0
            };
        }

        ushort dspWritePort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspWriteData);
        ushort dspReadPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspReadData);
        soundBlaster.WriteByte(dspWritePort, (byte)SoundBlaster.DspCommand.GetDspVersion);
        byte majorVersion = soundBlaster.ReadByte(dspReadPort);
        byte minorVersion = soundBlaster.ReadByte(dspReadPort);
        return new SoundBlasterDspVersionResponse {
            MajorVersion = majorVersion,
            MinorVersion = minorVersion
        };
    }

    private static byte ReadSoundBlasterMixerRegister(SoundBlaster soundBlaster, SoundBlaster.MixerRegister register) {
        return ReadSoundBlasterMixerRegister(soundBlaster, (byte)register);
    }

    private static byte ReadSoundBlasterMixerRegister(SoundBlaster soundBlaster, byte register) {
        ushort mixerIndexPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerIndex);
        ushort mixerDataPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerData);
        soundBlaster.WriteByte(mixerIndexPort, register);
        return soundBlaster.ReadByte(mixerDataPort);
    }

    private static void WriteSoundBlasterMixerRegister(SoundBlaster soundBlaster, byte register, byte value) {
        ushort mixerIndexPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerIndex);
        ushort mixerDataPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerData);
        soundBlaster.WriteByte(mixerIndexPort, register);
        soundBlaster.WriteByte(mixerDataPort, value);
    }

    private static VideoCursorStateResponse BuildVideoCursorResponse(CursorPosition cursorPosition, CharacterPlusAttribute character) {
        return new VideoCursorStateResponse {
            Page = cursorPosition.Page,
            X = cursorPosition.X,
            Y = cursorPosition.Y,
            Character = character.Character.ToString(),
            Attribute = character.Attribute,
            UseAttribute = character.UseAttribute
        };
    }

    private static void ValidateVideoPage(int page) {
        if (page < 0 || page > 7) {
            throw new ArgumentException("Video page must be between 0 and 7");
        }
    }

    private static void ValidateTextCoordinates(int page, int x, int y, BiosDataArea biosDataArea) {
        ValidateVideoPage(page);
        if (x < 0 || x >= biosDataArea.ScreenColumns) {
            throw new ArgumentException($"X must be between 0 and {biosDataArea.ScreenColumns - 1}");
        }

        int maximumRow = biosDataArea.ScreenRows;
        if (y < 0 || y > maximumRow) {
            throw new ArgumentException($"Y must be between 0 and {maximumRow}");
        }
    }

    private static void ValidatePixelCoordinates(int x, int y, VgaMode currentMode) {
        if (x < 0 || x >= currentMode.Width) {
            throw new ArgumentException($"X must be between 0 and {currentMode.Width - 1}");
        }
        if (y < 0 || y >= currentMode.Height) {
            throw new ArgumentException($"Y must be between 0 and {currentMode.Height - 1}");
        }
    }

    private static byte ResolveDosDriveNumber(Dos dos, string driveLetter, out string resolvedDrive) {
        if (string.IsNullOrWhiteSpace(driveLetter)) {
            resolvedDrive = dos.DosDriveManager.CurrentDrive.DosVolume;
            return 0;
        }

        if (driveLetter.Length != 1) {
            throw new ArgumentException("Drive letter must be empty or a single character between A and Z");
        }

        char normalizedDriveLetter = char.ToUpperInvariant(driveLetter[0]);
        if (!DosDriveManager.DriveLetters.TryGetValue(normalizedDriveLetter, out byte driveIndex)) {
            throw new ArgumentException($"Drive letter '{normalizedDriveLetter}' is invalid");
        }
        if (!dos.DosDriveManager.TryGetValue(normalizedDriveLetter, out VirtualDrive? virtualDrive) || virtualDrive == null) {
            throw new InvalidOperationException($"Drive '{normalizedDriveLetter}' is not mounted");
        }

        resolvedDrive = virtualDrive.DosVolume;
        return (byte)(driveIndex + 1);
    }

    private static void EnsureDosFileOperationSucceeded(DosFileOperationResult result, string context) {
        if (!result.IsError) {
            return;
        }

        DosErrorCode errorCode = (DosErrorCode)(result.Value ?? 0);
        throw new InvalidOperationException($"{context}: {errorCode}");
    }

    private static CpuStatusResponse BuildCpuStatus(State state) {
        CpuRegistersResponse registers = BuildCpuRegistersResponse(state);
        return new CpuStatusResponse {
            GeneralPurpose = registers.GeneralPurpose,
            Segments = registers.Segments,
            InstructionPointer = registers.InstructionPointer,
            Flags = registers.Flags,
            Cycles = state.Cycles
        };
    }

    private bool WaitUntilPaused(TimeSpan timeout) {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout) {
            if (_services.PauseHandler.IsPaused) {
                return true;
            }
            System.Threading.Thread.Sleep(1);
        }

        return _services.PauseHandler.IsPaused;
    }

    [McpManualControl]
    [McpServerTool(Name = "step_over", UseStructuredContent = true), Description("Step over one instruction. For CALL or INT, runs until the return address; otherwise behaves like step. Caller must supply nextAddress (CS*16+IP + instruction length) and isCallOrInterrupt.")]
    public CallToolResult StepOver(uint nextAddress, bool isCallOrInterrupt) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    _services.PauseHandler.RequestPause("Step over requested while running");
                }
                if (isCallOrInterrupt) {
                    DebuggerStepHelper.SetupStepOverBreakpoint(_services.BreakpointsManager, nextAddress,
                        () => _services.PauseHandler.RequestPause("Step over hit"));
                } else {
                    DebuggerStepHelper.SetupStepIntoBreakpoint(_services.BreakpointsManager, _services.State,
                        () => _services.PauseHandler.RequestPause("Step over hit"));
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Step over initiated" };
            }
        });
    }

    [McpServerTool(Name = "read_stack", UseStructuredContent = true), Description("Read the top values of the stack (SS:SP). Returns addresses and 16-bit values.")]
    public CallToolResult ReadStack(int count) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (count <= 0 || count > 100) {
                    throw new ArgumentException("Count must be between 1 and 100");
                }

                ushort ss = _services.State.SS;
                ushort sp = _services.State.SP;
                uint baseAddress = (uint)(ss << 4) + sp;

                List<StackValue> values = new();
                for (int i = 0; i < count; i++) {
                    uint addr = baseAddress + (uint)(i * 2);
                    if (addr + 1 >= _services.Memory.Length) break;
                    ushort val = _services.Memory.UInt16[addr];
                    values.Add(new StackValue { Address = addr, Value = val });
                }

                return new StackResponse { Ss = ss, Sp = sp, Values = values };
            }
        });
    }

    [McpServerTool(Name = "query_ems", UseStructuredContent = true), Description("Query EMS (Expanded Memory Manager) state")]
    public CallToolResult QueryEms() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }

                EmsHandleInfo[] handles = _services.EmsManager.EmmHandles
                    .Where(kvp => kvp.Key != ExpandedMemoryManager.EmmNullHandle && kvp.Value != null)
                    .Select(kvp => new EmsHandleInfo {
                        HandleId = kvp.Key,
                        AllocatedPages = kvp.Value.LogicalPages.Count,
                        Name = kvp.Value.Name
                    })
                    .ToArray();

                int allocatedPages = handles.Sum(h => h.AllocatedPages);
                ushort freePages = _services.EmsManager.GetFreePageCount();

                return new EmsStateResponse {
                    IsEnabled = true,
                    PageFrameSegment = ExpandedMemoryManager.EmmPageFrameSegment,
                    TotalPages = allocatedPages + freePages,
                    AllocatedPages = allocatedPages,
                    FreePages = freePages,
                    PageSize = ExpandedMemoryManager.EmmPageSize,
                    Handles = handles,
                    PageMappings = Enumerable.Range(0, ExpandedMemoryManager.EmmMaxPhysicalPages)
                        .Select(physicalPage => {
                            (bool isMapped, int? handleId, int? logicalPage) = ResolveEmsMappingForPhysicalPage((ushort)physicalPage);
                            return new EmsPageMappingInfo {
                                PhysicalPage = physicalPage,
                                Segment = ToPageSegment(physicalPage),
                                IsMapped = isMapped,
                                HandleId = handleId,
                                LogicalPage = logicalPage
                            };
                        })
                        .ToArray()
                };
            }
        });
    }

    [McpServerTool(Name = "read_ems_page_frame", UseStructuredContent = true), Description("Read mapped bytes from EMS page frame by physical page index (read-only).")]
    public CallToolResult ReadEmsPageFrame(int physicalPage, int offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }
                if (physicalPage < 0 || physicalPage >= ExpandedMemoryManager.EmmMaxPhysicalPages) {
                    throw new InvalidOperationException($"Physical page must be between 0 and {ExpandedMemoryManager.EmmMaxPhysicalPages - 1}");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }

                EmmPage page = _services.EmsManager.EmmPageFrame[(ushort)physicalPage].PhysicalPage;
                if (offset < 0 || offset >= page.Size) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > page.Size) {
                    throw new InvalidOperationException("Read would exceed page boundary");
                }

                IList<byte> data = page.GetSlice(offset, length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new EmsPageFrameReadResponse {
                    PhysicalPage = physicalPage,
                    Offset = offset,
                    Length = length,
                    Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "read_ems_memory", UseStructuredContent = true), Description("Read EMS (Expanded Memory) from a specific handle and page")]
    public CallToolResult ReadEmsMemory(int handle, int logicalPage, int offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }
                if (!_services.EmsManager.EmmHandles.TryGetValue(handle, out EmmHandle? emmHandle)) {
                    throw new InvalidOperationException($"Invalid EMS handle: {handle}");
                }
                if (logicalPage < 0 || logicalPage >= emmHandle.LogicalPages.Count) {
                    throw new InvalidOperationException($"Invalid logical page: {logicalPage}");
                }
                EmmPage page = emmHandle.LogicalPages[logicalPage];
                if (offset < 0 || offset >= page.Size) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > page.Size) {
                    throw new InvalidOperationException("Read would exceed page boundary");
                }

                IList<byte> data = page.GetSlice(offset, length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new EmsMemoryReadResponse {
                    Handle = handle, LogicalPage = logicalPage,
                    Offset = offset, Length = length,
                    Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "query_xms", UseStructuredContent = true), Description("Query XMS (Extended Memory Manager) state")]
    public CallToolResult QueryXms() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }

                List<XmsHandleInfo> handles = new();
                for (int handleId = 1; handleId <= 512; handleId++) {
                    if (!_services.XmsManager.TryGetBlock(handleId, out XmsBlock? block)) {
                        continue;
                    }

                    if (block == null) {
                        continue;
                    }

                    byte lockCount = GetXmsLockCount(handleId);
                    handles.Add(new XmsHandleInfo {
                        HandleId = handleId,
                        SizeKB = (int)(block.Value.Length / 1024),
                        IsLocked = lockCount > 0
                    });
                }

                long freeMemoryKB = _services.XmsManager.TotalFreeMemory / 1024;
                long largestBlockKB = _services.XmsManager.LargestFreeBlockLength / 1024;
                int totalMemoryKB = ExtendedMemoryManager.XmsMemorySize;

                return new XmsStateResponse {
                    IsEnabled = true,
                    TotalMemoryKB = totalMemoryKB,
                    FreeMemoryKB = (int)freeMemoryKB,
                    LargestBlockKB = (int)largestBlockKB,
                    HmaAvailable = true,
                    HmaAllocated = false,
                    AllocatedBlocks = handles.Count,
                    Handles = handles.ToArray()
                };
            }
        });
    }

    private byte GetXmsLockCount(int handleId) {
        ExtendedMemoryManager? xmsManager = _services.XmsManager;
        if (xmsManager == null) {
            return 0;
        }

        uint savedEax = _services.State.EAX;
        uint savedEbx = _services.State.EBX;
        uint savedEcx = _services.State.ECX;
        uint savedEdx = _services.State.EDX;

        try {
            _services.State.DX = (ushort)handleId;
            xmsManager.GetEmbHandleInformation();
            if (_services.State.AX != 1) {
                return 0;
            }

            return _services.State.BH;
        } finally {
            _services.State.EAX = savedEax;
            _services.State.EBX = savedEbx;
            _services.State.ECX = savedEcx;
            _services.State.EDX = savedEdx;
        }
    }

    [McpServerTool(Name = "read_xms_memory", UseStructuredContent = true), Description("Read XMS (Extended Memory) from a specific handle")]
    public CallToolResult ReadXmsMemory(int handle, uint offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }
                if (!_services.XmsManager.TryGetBlock(handle, out XmsBlock? xmsBlock)) {
                    throw new InvalidOperationException($"Invalid XMS handle: {handle}");
                }
                if (offset >= xmsBlock.Value.Length) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > xmsBlock.Value.Length) {
                    throw new InvalidOperationException("Read would exceed block boundary");
                }

                IList<byte> data = _services.XmsManager.XmsRam.GetSlice((int)(xmsBlock.Value.Offset + offset), length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new XmsMemoryReadResponse {
                    Handle = handle, Offset = offset,
                    Length = length, Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "search_ems_memory", UseStructuredContent = true), Description("Search a hex pattern in one EMS logical page (read-only).")]
    public CallToolResult SearchEmsMemory(int handle, int logicalPage, [StringSyntax("Hexadecimal")] string pattern, int startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                EmmPage page = ResolveEmsPage(handle, logicalPage);
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);
                int searchLength = ComputeSearchWindowLength(startOffset, length, (int)page.Size);
                uint[] matches = SearchArray(page.GetSlice(startOffset, searchLength), needle, (uint)startOffset, limit);
                return new EmsMemorySearchResponse {
                    Handle = handle,
                    LogicalPage = logicalPage,
                    Pattern = Convert.ToHexString(needle),
                    StartOffset = startOffset,
                    Length = searchLength,
                    Matches = matches.Select(static x => (int)x).ToArray(),
                    Truncated = matches.Length >= limit
                };
            }
        });
    }

    [McpServerTool(Name = "search_xms_memory", UseStructuredContent = true), Description("Search a hex pattern in one XMS block (read-only).")]
    public CallToolResult SearchXmsMemory(int handle, [StringSyntax("Hexadecimal")] string pattern, uint startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                XmsBlock block = ResolveXmsBlock(handle);
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);
                int searchLength = ComputeSearchWindowLength((int)startOffset, length, (int)block.Length);
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }
                IList<byte> data = _services.XmsManager.XmsRam.GetSlice((int)(block.Offset + startOffset), searchLength);
                uint[] matches = SearchArray(data, needle, startOffset, limit);
                return new XmsMemorySearchResponse {
                    Handle = handle,
                    Pattern = Convert.ToHexString(needle),
                    StartOffset = startOffset,
                    Length = searchLength,
                    Matches = matches,
                    Truncated = matches.Length >= limit
                };
            }
        });
    }

    private EmmPage ResolveEmsPage(int handle, int logicalPage) {
        if (_services.EmsManager == null) {
            throw new InvalidOperationException("EMS is not enabled");
        }
        if (!_services.EmsManager.EmmHandles.TryGetValue(handle, out EmmHandle? emmHandle)) {
            throw new InvalidOperationException($"Invalid EMS handle: {handle}");
        }
        if (logicalPage < 0 || logicalPage >= emmHandle.LogicalPages.Count) {
            throw new InvalidOperationException($"Invalid logical page: {logicalPage}");
        }
        return emmHandle.LogicalPages[logicalPage];
    }

    private XmsBlock ResolveXmsBlock(int handle) {
        if (_services.XmsManager == null) {
            throw new InvalidOperationException("XMS is not enabled");
        }
        if (!_services.XmsManager.TryGetBlock(handle, out XmsBlock? block) || block == null) {
            throw new InvalidOperationException($"Invalid XMS handle: {handle}");
        }
        return block.Value;
    }

    private static int ComputeSearchWindowLength(int startOffset, int requestedLength, int maxLength) {
        if (startOffset < 0 || startOffset >= maxLength) {
            throw new InvalidOperationException($"Invalid offset: {startOffset}");
        }

        int availableLength = maxLength - startOffset;
        if (requestedLength <= 0) {
            return availableLength;
        }
        return Math.Min(requestedLength, availableLength);
    }

    private static uint[] SearchArray(IList<byte> data, byte[] needle, uint baseOffset, int limit) {
        if (data.Count < needle.Length) {
            return Array.Empty<uint>();
        }

        List<uint> matches = new();
        int lastSearchIndex = data.Count - needle.Length;
        for (int i = 0; i <= lastSearchIndex && matches.Count < limit; i++) {
            bool matched = true;
            for (int j = 0; j < needle.Length; j++) {
                if (data[i + j] != needle[j]) {
                    matched = false;
                    break;
                }
            }
            if (matched) {
                matches.Add(baseOffset + (uint)i);
            }
        }
        return matches.ToArray();
    }

    [McpServerTool(Name = "add_breakpoint", UseStructuredContent = true), Description("Add a breakpoint (execution, memory, or IO). Valid types: CPU_EXECUTION_ADDRESS, MEMORY_ACCESS, MEMORY_WRITE, MEMORY_READ, IO_ACCESS, IO_WRITE, IO_READ (case-insensitive). Pass null for condition to add an unconditional breakpoint.")]
    public CallToolResult AddBreakpoint(long address, string type, [StringSyntax("Spice86BreakpointCondition")] string? condition) {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                if (!Enum.TryParse(type, true, out BreakPointType bpType)) {
                    string validTypes = string.Join(", ", Enum.GetNames<BreakPointType>());
                    throw new ArgumentException($"Invalid breakpoint type: '{type}'. Valid types: {validTypes}");
                }

                string id = _services.GetNextBreakpointId().ToString();
                Action<BreakPoint> onReached = _ => {
                    _services.PauseHandler.RequestPause($"MCP Breakpoint {id} hit");
                };

                BreakPoint breakPoint;
                if (string.IsNullOrWhiteSpace(condition)) {
                    breakPoint = new AddressBreakPoint(bpType, address, onReached, false);
                } else {
                    BreakpointConditionCompiler compiler = new(_services.State,
                        _services.Memory as Memory ?? throw new InvalidOperationException("Memory must be of type Memory"));
                    Func<long, bool> compiledCondition = compiler.Compile(condition);
                    breakPoint = new AddressBreakPoint(bpType, address, onReached, false, compiledCondition, condition);
                }

                _services.BreakpointsManager.ToggleBreakPoint(breakPoint, true);
                _services.McpBreakpoints[id] = breakPoint;

                return new BreakpointInfo {
                    Id = id, Address = address,
                    Type = bpType.ToString(), Condition = condition,
                    IsEnabled = true
                };
            }
        });
    }

    [McpServerTool(Name = "list_breakpoints", UseStructuredContent = true), Description("List all MCP-managed breakpoints")]
    public CallToolResult ListBreakpoints() {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                List<BreakpointInfo> list = _services.McpBreakpoints.Select(kvp => new BreakpointInfo {
                    Id = kvp.Key,
                    Address = (kvp.Value as AddressBreakPoint)?.Address ?? 0,
                    Type = kvp.Value.BreakPointType.ToString(),
                    Condition = (kvp.Value as AddressBreakPoint)?.ConditionExpression,
                    IsEnabled = kvp.Value.IsEnabled
                }).ToList();

                return new BreakpointListResponse { Breakpoints = list };
            }
        });
    }

    [McpServerTool(Name = "remove_breakpoint", UseStructuredContent = true), Description("Remove an MCP-managed breakpoint by ID")]
    public CallToolResult RemoveBreakpoint(string id) {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                if (_services.McpBreakpoints.Remove(id, out BreakPoint? bp)) {
                    _services.BreakpointsManager.RemoveUserBreakpoint(bp);
                    return new EmulatorControlResponse { Success = true, Message = $"Breakpoint {id} removed." };
                }
                throw new ArgumentException($"Breakpoint {id} not found.");
            }
        });
    }

    [McpServerTool(Name = "clear_breakpoints", UseStructuredContent = true), Description("Remove ALL breakpoints currently managed by the MCP server.")]
    public CallToolResult ClearBreakpoints() {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                int count = _services.McpBreakpoints.Count;
                _services.BreakpointsManager.RemoveBreakpoints(_services.McpBreakpoints.Values);
                _services.McpBreakpoints.Clear();
                return new EmulatorControlResponse { Success = true, Message = $"{count} breakpoints removed." };
            }
        });
    }
}
