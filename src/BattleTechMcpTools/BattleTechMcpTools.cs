using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Shared.Utils;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleTechMcpTools;

[McpServerToolType]
public sealed class BattleTechMcpTools
{
    // DS-relative offsets for BattleTech data structures.
    // Physical address computed at runtime as (DS << 4) + Offset.
    private const ushort StateArrayOff = 0xD30C;
    private const ushort StorySlotsOff = 0xC724;
    private const ushort UnitSlotsOff = 0xC614;
    private const ushort CursorXOff = 0xA44B;
    private const ushort CursorYOff = 0xA44D;
    private const ushort CreditsOff = 0xD370;
    private const ushort FogGridAOff = 0x40B4;
    private const ushort FogGridBOff = 0x41D4;
    private const ushort CombatUnitXOff = 0x4004;
    private const ushort CombatUnitYOff = 0x4036;
    private const ushort CombatUnitStatusOff = 0x406A;
    private const ushort TrainingCompleteOff = 0xD450;
    private const ushort MilestoneOff = 0xD451;

    private const int StateArraySize = 256;
    private const int StorySlotSize = 125;
    private const int StorySlotCount = 8;
    private const int UnitSlotSize = 17;
    private const int UnitSlotCount = 8;
    private const int CombatUnitCount = 24;
    private const int FogGridRows = 12;
    private const int FogGridCols = 24;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly EmulatorMcpServices _services;

    public BattleTechMcpTools(EmulatorMcpServices services)
    {
        _services = services;
    }

    private CallToolResult Success(object response)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(
            response, response.GetType(), SerializerOptions);
        return new CallToolResult { StructuredContent = structuredContent };
    }

    private CallToolResult Error(string message)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(
            new { success = false, message }, SerializerOptions);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = structuredContent,
            Content = [new TextContentBlock { Text = message }]
        };
    }

    private CallToolResult ExecuteTool(
        Func<object> action, [CallerMemberName] string methodName = "")
    {
        try
        {
            return Success(action());
        }
        catch (ArgumentException ex) { return Error(ex.Message); }
        catch (InvalidOperationException ex) { return Error(ex.Message); }
        catch (KeyNotFoundException ex) { return Error(ex.Message); }
        catch (FormatException ex) { return Error(ex.Message); }
        catch (OverflowException ex) { return Error(ex.Message); }
    }

    private ushort DsSegment => _services.State.DS;
    private uint DsAddr(ushort off) => (uint)((DsSegment << 4) + off);
    private IMemory Memory => _services.Memory;

    // ──────────────────────────────────────────────
    //  BattleTech State Tools
    // ──────────────────────────────────────────────

    [McpServerTool(Name = "bt_read_state_array", UseStructuredContent = true)]
    [Description("Read the 256-byte BattleTech StateArray (DS:0xD30C). Returns hex dump + named fields.")]
    public CallToolResult ReadStateArray()
    {
        return ExecuteTool(() =>
        {
            uint addr = DsAddr(StateArrayOff);
            byte[] data = Memory.GetData(addr, StateArraySize);
            return new
            {
                Segmented = $"DS:0x{StateArrayOff:X4}",
                Physical = $"0x{addr:X}",
                Size = StateArraySize,
                Hex = Convert.ToHexString(data),
                Fields = new
                {
                    TrainingComplete = data[0x50],
                    Milestone = data[0x51],
                    CacheFound = data[0x52],
                    WinScene = data[0x53],
                    ShopSelectionSlot = data[0x14],
                    StoryStateVariant = data[0x0E],
                    EncounterMask = data[0x24],
                    DecrementTarget = data[0x23]
                }
            };
        });
    }

    [McpServerTool(Name = "bt_write_state_array", UseStructuredContent = true)]
    [Description("Write bytes to BattleTech StateArray (DS:0xD30C). Params: offset (0-255), data (hex string).")]
    public CallToolResult WriteStateArray(int offset, string data)
    {
        return ExecuteTool(() =>
        {
            if (offset < 0 || offset >= StateArraySize)
                throw new ArgumentException($"Offset must be 0-{StateArraySize - 1}");
            byte[] bytes = Convert.FromHexString(data);
            if (offset + bytes.Length > StateArraySize)
                throw new ArgumentException("Data overflow");
            uint baseAddr = DsAddr(StateArrayOff);
            for (int i = 0; i < bytes.Length; i++)
                Memory.UInt8[baseAddr + (uint)(offset + i)] = bytes[i];
            byte[] readBack = Memory.GetData(baseAddr + (uint)offset, (uint)bytes.Length);
            return new
            {
                Segmented = $"DS:0x{StateArrayOff + (ushort)offset:X4}",
                Written = Convert.ToHexString(bytes),
                ReadBack = Convert.ToHexString(readBack)
            };
        });
    }

    [McpServerTool(Name = "bt_read_story_slot", UseStructuredContent = true)]
    [Description("Read a story slot (DS:0xC724 + index*0x7D). Index 0-7. Returns hex + parsed fields.")]
    public CallToolResult ReadStorySlot(int slotIndex)
    {
        return ExecuteTool(() =>
        {
            if (slotIndex < 0 || slotIndex >= StorySlotCount)
                throw new ArgumentException($"Slot index 0-{StorySlotCount - 1}");
            uint slotOff = (uint)(StorySlotsOff + slotIndex * StorySlotSize);
            uint addr = DsAddr((ushort)slotOff);
            byte[] data = Memory.GetData(addr, StorySlotSize);
            string name = ReadPaddedString(addr, 16);
            return new
            {
                SlotIndex = slotIndex,
                Segmented = $"DS:0x{slotOff:X4}",
                Physical = $"0x{addr:X}",
                Hex = Convert.ToHexString(data),
                StoryFields = new
                {
                    StatusByte = data[0x00],
                    FlagsLow = data[0x04],
                    FlagsHigh = data[0x05],
                    TimingNibble = data[0x06],
                    CounterA = data[0x55],
                    CounterB = data[0x56],
                    StoryState = data[0x57],
                    LatchMarker = data[0x58],
                    LinkedUnitSlot = data[0x79],
                    SecondaryUnitSlot = data[0x7A],
                    MechID = data[0x7B]
                },
                MechFields = new
                {
                    Name = name,
                    Tonnage = data[0x10],
                    CurrentArmour = ToByteArray(data, 0x11, 11),
                    CurrentStructure = ToByteArray(data, 0x1C, 8),
                    CurrentAmmo = ToByteArray(data, 0x27, 10),
                    WalkMove = data[0x33],
                    JumpMove = data[0x34],
                    MaxArmour = ToByteArray(data, 0x58, 11),
                    MaxStructure = ToByteArray(data, 0x63, 8),
                    MaxAmmo = ToByteArray(data, 0x6F, 10)
                }
            };
        });
    }

    [McpServerTool(Name = "bt_read_unit_slot", UseStructuredContent = true)]
    [Description("Read a unit slot (DS:0xC614 + index*0x11). Index 0-7. Returns TypeId, attrs, inventory.")]
    public CallToolResult ReadUnitSlot(int slotIndex)
    {
        return ExecuteTool(() =>
        {
            if (slotIndex < 0 || slotIndex >= UnitSlotCount)
                throw new ArgumentException($"Slot index 0-{UnitSlotCount - 1}");
            uint slotOff = (uint)(UnitSlotsOff + slotIndex * UnitSlotSize);
            uint addr = DsAddr((ushort)slotOff);
            byte[] data = Memory.GetData(addr, UnitSlotSize);
            return new
            {
                SlotIndex = slotIndex,
                Segmented = $"DS:0x{slotOff:X4}",
                Physical = $"0x{addr:X}",
                TypeId = data[0x00],
                IsEmpty = data[0x00] == 0xFF,
                Attr1 = data[0x01],
                Attr2 = data[0x02],
                Attr3 = data[0x03],
                Inventory = new { S0 = data[0x04], S1 = data[0x05], S2 = data[0x06],
                                  S3 = data[0x07], S4 = data[0x08], S5 = data[0x09], S6 = data[0x0A] },
                LinkedStorySlot = data[0x0C],
                DerivedAttr = data[0x0F]
            };
        });
    }

    [McpServerTool(Name = "bt_read_cursor", UseStructuredContent = true)]
    [Description("Read world map cursor (DS:0xA44B/A44D, two uint16). Returns raw + tile coords.")]
    public CallToolResult ReadCursor()
    {
        return ExecuteTool(() =>
        {
            ushort rawX = Memory.UInt16[DsAddr(CursorXOff)];
            ushort rawY = Memory.UInt16[DsAddr(CursorYOff)];
            return new
            {
                SegmentedX = $"DS:0x{CursorXOff:X4}",
                SegmentedY = $"DS:0x{CursorYOff:X4}",
                RawX = rawX,
                RawY = rawY,
                TileX = (rawX & 0x7F) >> 1,
                TileY = (rawY & 0x7F) >> 1,
                TileIndex = ((rawY & 0x7F) >> 1) * 64 + ((rawX & 0x7F) >> 1),
                HexX = $"0x{rawX:X4}",
                HexY = $"0x{rawY:X4}"
            };
        });
    }

    [McpServerTool(Name = "bt_read_credits", UseStructuredContent = true)]
    [Description("Read C-Bills (DS:0xD370, uint32). Returns integer value.")]
    public CallToolResult ReadCredits()
    {
        return ExecuteTool(() =>
        {
            uint credits = Memory.UInt32[DsAddr(CreditsOff)];
            return new { Credits = (int)credits, Hex = $"0x{credits:X8}" };
        });
    }

    [McpServerTool(Name = "bt_read_flags", UseStructuredContent = true)]
    [Description("Read TrainingComplete (DS:0xD450) and Milestone (DS:0xD451) flags.")]
    public CallToolResult ReadFlags()
    {
        return ExecuteTool(() =>
        {
            byte training = Memory.UInt8[DsAddr(TrainingCompleteOff)];
            byte milestone = Memory.UInt8[DsAddr(MilestoneOff)];
            return new
            {
                TrainingComplete = training != 0,
                TrainingByte = training,
                Milestone = milestone != 0,
                MilestoneByte = milestone
            };
        });
    }

    [McpServerTool(Name = "bt_read_combat_grids", UseStructuredContent = true)]
    [Description("Read both combat fog grids (12×24, DS:0x40B4 and 0x41D4). Returns 2D arrays.")]
    public CallToolResult ReadCombatGrids()
    {
        return ExecuteTool(() =>
        {
            byte[] a = Memory.GetData(DsAddr(FogGridAOff), FogGridRows * FogGridCols);
            byte[] b = Memory.GetData(DsAddr(FogGridBOff), FogGridRows * FogGridCols);
            return new
            {
                GridA = new
                {
                    Segmented = $"DS:0x{FogGridAOff:X4}",
                    Rows = FogGridRows,
                    Cols = FogGridCols,
                    Data = To2DArray(a, FogGridRows, FogGridCols),
                    Hex = Convert.ToHexString(a)
                },
                GridB = new
                {
                    Segmented = $"DS:0x{FogGridBOff:X4}",
                    Rows = FogGridRows,
                    Cols = FogGridCols,
                    Data = To2DArray(b, FogGridRows, FogGridCols),
                    Hex = Convert.ToHexString(b)
                }
            };
        });
    }

    [McpServerTool(Name = "bt_read_combat_units", UseStructuredContent = true)]
    [Description("Read 24 combat unit positions/statuses (DS:0x4004, 0x4036, 0x406A).")]
    public CallToolResult ReadCombatUnits()
    {
        return ExecuteTool(() =>
        {
            var units = new List<object>();
            for (int i = 0; i < CombatUnitCount; i++)
            {
                ushort x = Memory.UInt16[DsAddr((ushort)(CombatUnitXOff + i * 2))];
                ushort y = Memory.UInt16[DsAddr((ushort)(CombatUnitYOff + i * 2))];
                ushort status = Memory.UInt16[DsAddr((ushort)(CombatUnitStatusOff + i * 2))];
                units.Add(new { UnitId = i, X = x, Y = y, Status = status, IsActive = status != 0 });
            }
            return new { Units = units };
        });
    }

    [McpServerTool(Name = "bt_get_state", UseStructuredContent = true)]
    [Description("Comprehensive game state snapshot: state array, cursor, credits, flags, active counts.")]
    public CallToolResult GetState()
    {
        return ExecuteTool(() =>
        {
            byte[] stateArr = Memory.GetData(DsAddr(StateArrayOff), 64);
            ushort rawX = Memory.UInt16[DsAddr(CursorXOff)];
            ushort rawY = Memory.UInt16[DsAddr(CursorYOff)];
            uint credits = Memory.UInt32[DsAddr(CreditsOff)];
            byte training = Memory.UInt8[DsAddr(TrainingCompleteOff)];
            byte milestone = Memory.UInt8[DsAddr(MilestoneOff)];

            int activeSlots = 0;
            for (int i = 0; i < StorySlotCount; i++)
                if (Memory.UInt8[DsAddr((ushort)(StorySlotsOff + i * StorySlotSize))] != 0xFF)
                    activeSlots++;

            int activeUnits = 0;
            for (int i = 0; i < CombatUnitCount; i++)
                if (Memory.UInt16[DsAddr((ushort)(CombatUnitStatusOff + i * 2))] != 0)
                    activeUnits++;

            return new
            {
                StateArrayHex = Convert.ToHexString(stateArr),
                Cursor = new
                {
                    RawX = rawX, RawY = rawY,
                    TileX = (rawX & 0x7F) >> 1,
                    TileY = (rawY & 0x7F) >> 1
                },
                Credits = credits,
                Flags = new { TrainingComplete = training != 0, Milestone = milestone != 0 },
                ActiveStorySlots = activeSlots,
                ActiveCombatUnits = activeUnits,
                DsSegment = DsSegment
            };
        });
    }

    [McpServerTool(Name = "bt_read_registers", UseStructuredContent = true)]
    [Description("Read current emulated CPU registers: segment regs (CS, DS, ES, FS, GS, SS), general regs (AX, BX, CX, DX, SI, DI, BP, SP), and IP.")]
    public CallToolResult ReadRegisters()
    {
        return ExecuteTool(() =>
        {
            var s = _services.State;
            return new
            {
                Segments = new { CS = s.CS, DS = s.DS, ES = s.ES, FS = s.FS, GS = s.GS, SS = s.SS },
                Registers16 = new { AX = s.AX, BX = s.BX, CX = s.CX, DX = s.DX, SI = s.SI, DI = s.DI, BP = s.BP, SP = s.SP },
                IP = s.IP,
                PhysicalIP = $"0x{s.IpPhysicalAddress:X}",
                ZeroFlag = s.ZeroFlag,
                CarryFlag = s.CarryFlag,
                Cycles = s.Cycles
            };
        });
    }

    // ──────────────────────────────────────────────
    //  Generic Memory Tools
    // ──────────────────────────────────────────────

    [McpServerTool(Name = "bt_read_memory", UseStructuredContent = true)]
    [Description("Read raw emulated memory at a physical address. Params: address (uint), length (1-65536). Returns hex dump.")]
    public CallToolResult ReadMemory(uint address, int length)
    {
        return ExecuteTool(() =>
        {
            if (length <= 0 || length > 65536)
                throw new ArgumentException("Length 1-65536");
            if (address + (uint)length > Memory.Length)
                throw new ArgumentException("Exceeds memory bounds");
            byte[] data = Memory.GetData(address, (uint)length);
            return new
            {
                Address = $"0x{address:X}",
                Length = length,
                Hex = Convert.ToHexString(data),
                Segmented = FormatSegmented(address)
            };
        });
    }

    [McpServerTool(Name = "bt_write_memory", UseStructuredContent = true)]
    [Description("Write bytes to emulated memory at a physical address. Params: address (uint), data (hex string). Returns readback.")]
    public CallToolResult WriteMemory(uint address, string data)
    {
        return ExecuteTool(() =>
        {
            byte[] bytes = Convert.FromHexString(data);
            if (address + (uint)bytes.Length > Memory.Length)
                throw new ArgumentException("Exceeds memory bounds");
            for (int i = 0; i < bytes.Length; i++)
                Memory.UInt8[address + (uint)i] = bytes[i];
            byte[] readBack = Memory.GetData(address, (uint)bytes.Length);
            return new
            {
                Address = $"0x{address:X}",
                Written = Convert.ToHexString(bytes),
                ReadBack = Convert.ToHexString(readBack)
            };
        });
    }

    [McpServerTool(Name = "bt_read_ds", UseStructuredContent = true)]
    [Description("Read memory at a DS-relative offset. Params: offset (ushort, hex e.g. 0xD30C), length (1-65536). Returns hex dump at (DS<<4)+offset.")]
    public CallToolResult ReadDs(ushort offset, int length)
    {
        return ExecuteTool(() =>
        {
            if (length <= 0 || length > 65536)
                throw new ArgumentException("Length 1-65536");
            uint addr = DsAddr(offset);
            byte[] data = Memory.GetData(addr, (uint)length);
            return new
            {
                Segmented = $"DS:0x{offset:X4}",
                Physical = $"0x{addr:X}",
                Ds = DsSegment,
                Length = length,
                Hex = Convert.ToHexString(data)
            };
        });
    }

    [McpServerTool(Name = "bt_read_string", UseStructuredContent = true)]
    [Description("Read null-terminated string at a physical address. Params: address (uint), maxLength (default 256).")]
    public CallToolResult ReadString(uint address, int maxLength = 256)
    {
        return ExecuteTool(() =>
        {
            if (maxLength <= 0 || maxLength > 4096)
                throw new ArgumentException("maxLength 1-4096");
            string str = Memory.GetZeroTerminatedString(address, maxLength);
            return new
            {
                Address = $"0x{address:X}",
                Length = str.Length,
                Text = str,
                Segmented = FormatSegmented(address)
            };
        });
    }

    // ──────────────────────────────────────────────
    //  Keyboard Input Tools
    // ──────────────────────────────────────────────

    [McpServerTool(Name = "bt_send_key", UseStructuredContent = true)]
    [Description("Send a keyboard key event. Params: key (string, e.g. 'Enter', 'Escape', 'F1', 'A'), isPressed (bool). For press-release call twice.")]
    public CallToolResult SendKey(string key, bool isPressed)
    {
        return ExecuteTool(() =>
        {
            if (!Enum.TryParse(key, true, out PcKeyboardKey parsedKey))
                throw new ArgumentException($"Invalid key: '{key}'");
            PhysicalKey pk = KeyboardScancodeConverter.ConvertToPhysicalKey(parsedKey);
            if (pk == PhysicalKey.None)
                throw new ArgumentException($"No mapping for '{key}'");
            InputEventHub? hub = _services.InputEventHub;
            if (hub == null)
                throw new InvalidOperationException("InputEventHub not available");
            hub.PostKeyboardEvent(new KeyboardEventArgs(pk, isPressed));
            return new { Success = true, Key = key, Action = isPressed ? "down" : "up" };
        });
    }

    [McpServerTool(Name = "bt_type_text", UseStructuredContent = true)]
    [Description("Type a text string via keyboard events. Only printable ASCII letters/digits/space/simple punctuation.")]
    public CallToolResult TypeText(string text)
    {
        return ExecuteTool(() =>
        {
            InputEventHub? hub = _services.InputEventHub;
            if (hub == null)
                throw new InvalidOperationException("InputEventHub not available");
            int count = 0;
            foreach (char ch in text)
            {
                if (TryGetPhysicalKey(ch, out PhysicalKey pk))
                {
                    hub.PostKeyboardEvent(new KeyboardEventArgs(pk, true));
                    hub.PostKeyboardEvent(new KeyboardEventArgs(pk, false));
                    count++;
                }
            }
            return new { Success = true, CharactersTyped = count, Text = text };
        });
    }

    [McpServerTool(Name = "bt_press_enter", UseStructuredContent = true)]
    [Description("Press and release the Enter key.")]
    public CallToolResult PressEnter()
    {
        return ExecuteTool(() =>
        {
            InputEventHub? hub = _services.InputEventHub;
            if (hub == null)
                throw new InvalidOperationException("InputEventHub not available");
            PhysicalKey enter = KeyboardScancodeConverter.ConvertToPhysicalKey(PcKeyboardKey.Enter);
            hub.PostKeyboardEvent(new KeyboardEventArgs(enter, true));
            hub.PostKeyboardEvent(new KeyboardEventArgs(enter, false));
            return new { Success = true, Action = "Enter pressed" };
        });
    }

    [McpServerTool(Name = "bt_press_escape", UseStructuredContent = true)]
    [Description("Press and release the Escape key.")]
    public CallToolResult PressEscape()
    {
        return ExecuteTool(() =>
        {
            InputEventHub? hub = _services.InputEventHub;
            if (hub == null)
                throw new InvalidOperationException("InputEventHub not available");
            PhysicalKey esc = KeyboardScancodeConverter.ConvertToPhysicalKey(PcKeyboardKey.Escape);
            hub.PostKeyboardEvent(new KeyboardEventArgs(esc, true));
            hub.PostKeyboardEvent(new KeyboardEventArgs(esc, false));
            return new { Success = true, Action = "Escape pressed" };
        });
    }

    [McpServerTool(Name = "bt_inject_key", UseStructuredContent = true)]
    [Description("Directly inject a key into the BIOS keyboard buffer. "
        + "Pauses emulator for atomic access. Params: ascii (int 0-255), scanCode (int 0-255). "
        + "Key code = (scanCode << 8) | ascii. "
        + "Standard keys: ascii=char code, scanCode=PC scancode. "
        + "Extended keys (arrows): ascii=0, scanCode=0x48/0x50/0x4B/0x4D (up/down/left/right).")]
    public CallToolResult InjectKey(int ascii, int scanCode)
    {
        return ExecuteTool(() =>
        {
            if (ascii < 0 || ascii > 255)
                throw new ArgumentException("ascii must be 0-255");
            if (scanCode < 0 || scanCode > 255)
                throw new ArgumentException("scanCode must be 0-255");

            ushort keyCode = (ushort)((scanCode << 8) | ascii);

            var pauseHandler = _services.PauseHandler;
            var buffer = _services.BiosKeyboardBuffer;

            // Pause emulator for atomic buffer access
            pauseHandler.RequestPause("bt_inject_key");

            // Spin-wait for emulator to actually pause
            int timeout = 500;
            while (!pauseHandler.IsPaused && timeout > 0)
            {
                System.Threading.Thread.Sleep(1);
                timeout--;
            }

            bool paused = pauseHandler.IsPaused;
            bool result = false;
            if (paused && buffer != null)
            {
                result = buffer.EnqueueKeyCode(keyCode);
                pauseHandler.Resume();
            }
            else if (buffer == null)
            {
                // Fallback: write directly via Memory at physical 0x041E-0x043D
                // Use same circular buffer logic as BiosKeyboardBuffer
                pauseHandler.Resume(); // Already not paused or we resume
                result = FallbackInjectKey(keyCode);
            }
            else
            {
                pauseHandler.Resume();
            }

            return new
            {
                Success = result,
                KeyCode = $"0x{keyCode:X4}",
                Ascii = (byte)ascii,
                ScanCode = (byte)scanCode,
                WasPaused = paused,
                Char = ascii >= 32 && ascii <= 126 ? ((char)ascii).ToString() : null
            };
        });
    }

    [McpServerTool(Name = "bt_press_key", UseStructuredContent = true)]
    [Description("Press and release a key (injects ascii+scanCode into BIOS buffer twice).")]
    public CallToolResult PressKey(int ascii, int scanCode)
    {
        return ExecuteTool(() =>
        {
            var pauseHandler = _services.PauseHandler;
            var buffer = _services.BiosKeyboardBuffer;

            pauseHandler.RequestPause("bt_press_key");

            int timeout = 500;
            while (!pauseHandler.IsPaused && timeout > 0)
            {
                System.Threading.Thread.Sleep(1);
                timeout--;
            }

            bool result = false;
            if (pauseHandler.IsPaused && buffer != null)
            {
                ushort keyCode = (ushort)((scanCode << 8) | ascii);
                result = buffer.EnqueueKeyCode(keyCode);
                result = buffer.EnqueueKeyCode(keyCode) && result;
                pauseHandler.Resume();
            }
            else
            {
                pauseHandler.Resume();
            }

            return new
            {
                Success = result,
                Ascii = (byte)ascii,
                ScanCode = (byte)scanCode,
                Char = ascii >= 32 && ascii <= 126 ? ((char)ascii).ToString() : null
            };
        });
    }

    private bool FallbackInjectKey(ushort keyCode)
    {
        const ushort bufStart = 0x041E;
        const ushort bufEnd = 0x043E;

        ushort tail = _services.Memory.UInt16[0x041C];
        ushort head = _services.Memory.UInt16[0x041A];

        if (tail < bufStart || tail >= bufEnd)
            tail = bufStart;

        ushort newTail = (ushort)(tail + 2);
        if (newTail >= bufEnd)
            newTail = bufStart;

        if (newTail == head)
            return false;

        _services.Memory.UInt16[0, tail] = keyCode;
        _services.Memory.UInt16[0x041C] = newTail;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private string ReadPaddedString(uint addr, int maxLen)
    {
        Span<byte> buf = stackalloc byte[maxLen];
        for (int i = 0; i < maxLen; i++)
            buf[i] = Memory.UInt8[addr + (uint)i];
        int nullIdx = buf.IndexOf((byte)0);
        int len = nullIdx >= 0 ? nullIdx : maxLen;
        return System.Text.Encoding.ASCII.GetString(buf[..len]);
    }

    private static byte[] ToByteArray(byte[] data, int offset, int count)
    {
        byte[] result = new byte[count];
        Array.Copy(data, offset, result, 0, count);
        return result;
    }

    private static int[][] To2DArray(byte[] flat, int rows, int cols)
    {
        int[][] result = new int[rows][];
        for (int r = 0; r < rows; r++)
        {
            result[r] = new int[cols];
            for (int c = 0; c < cols; c++)
                result[r][c] = flat[r * cols + c];
        }
        return result;
    }

    private static string FormatSegmented(uint physicalAddress)
    {
        ushort segment = MemoryUtils.ToSegment(physicalAddress);
        ushort offset = (ushort)(physicalAddress & 0x0F);
        return $"{segment:X4}:{offset:X4}";
    }

    private static bool TryGetPhysicalKey(char ch, out PhysicalKey key)
    {
        key = PhysicalKey.None;
        if (ch >= 'a' && ch <= 'z')
            return TryParseKey($"A{char.ToUpperInvariant(ch)}", out key);
        if (ch >= 'A' && ch <= 'Z')
            return TryParseKey($"A{ch}", out key);
        if (ch >= '0' && ch <= '9')
            return TryParseKey($"D{ch}", out key);
        return ch switch
        {
            ' ' => TryParseKey("Space", out key),
            '.' => TryParseKey("Period", out key),
            ',' => TryParseKey("Comma", out key),
            '-' => TryParseKey("Minus", out key),
            '=' => TryParseKey("Equals", out key),
            '/' => TryParseKey("Slash", out key),
            ';' => TryParseKey("Semicolon", out key),
            '\'' => TryParseKey("Apostrophe", out key),
            '[' => TryParseKey("LeftBracket", out key),
            ']' => TryParseKey("RightBracket", out key),
            '\\' => TryParseKey("Backslash", out key),
            '`' => TryParseKey("GraveAccent", out key),
            '\t' => TryParseKey("Tab", out key),
            '\n' => TryParseKey("Enter", out key),
            _ => false
        };
    }

    private static bool TryParseKey(string name, out PhysicalKey key)
    {
        if (Enum.TryParse(name, true, out PcKeyboardKey pk))
        {
            key = KeyboardScancodeConverter.ConvertToPhysicalKey(pk);
            return key != PhysicalKey.None;
        }
        key = PhysicalKey.None;
        return false;
    }
}
