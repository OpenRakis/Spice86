---
name: spice86-config
description: "On-demand reference for starting Spice86 and understanding every Configuration.cs CLI option. Use when: reading or modifying Configuration.cs, adding new CLI flags, debugging startup behavior, writing tasks that set CLI options, or explaining how Spice86 launches with specific settings."
---

# Spice86 Configuration Reference

## Startup Basics

```bash
# Minimal launch
dotnet run --project src/Spice86 -- -e path\to\program.exe

# With C drive and debug pause
dotnet run --project src/Spice86 -- -c C:\dos -e program.exe --Debug

# Headless (no UI)
dotnet run --project src/Spice86 -- -e program.exe -h headless

# Disable all default servers for parallel runs
dotnet run --project src/Spice86 -- -e program.exe --HttpApiPort 0 --McpHttpPort 0 --GdbPort 0
```

## Configuration Options by Subsystem

### CPU & Execution
| Option | Default | Purpose |
|--------|---------|---------|
| `--CpuModel` | INTEL_80286 | Which x86 CPU to emulate |
| `--Cycles` | (none) | Cycles per ms (overrides instructions/sec) |
| `--JitMode` | InterpretedThenCompiled | JIT compilation strategy: interpreted, compiled-only, or hybrid |
| `--JitGeneratorIr` | false | Execute AST after IR round-trip (testing only) |
| `--StopAfterCycles` | 0 | Auto-stop after N cycles (cycle breakpoint) |
| `--CpuHeavyLog` | false | Log every instruction to file (slow) |
| `--CpuHeavyLogDumpFile` | (auto) | Custom path for heavy log output |
| `--CpuHeavyLogExpressions` | (none) | Named expressions per line: `"name=expression"` |
| `--FailOnInvalidOpcode` | true | Crash on invalid opcode vs. handle as fault |
| `--AllowIvtAddress0` | false | Treat INT to 0:0 as valid (continues at addr 0) |
| `--EnableSpeculativeCfgExploration` | true | Pre-explore unobserved CFG branches statically |
| `--RenderingMode` | Async | VGA events: Sync (determinism) or Async (performance) |
| `--AsmRenderingStyle` | Spice86 | How assembly is rendered in dumps |

### Memory & Hardware
| Option | Default | Purpose |
|--------|---------|---------|
| `--A20Gate` | false | Enable A20 gate (memory wrapping at 1MB) |
| `--Ems` | true | Expanded Memory Specification |
| `--Xms` | true | Extended Memory Specification |
| `--Umb` | true | DOS Upper Memory Blocks |
| `--ProgramEntryPointSegment` | 0x170 | Where the program loads (PSP/MCB created before) |
| `--ProvidedAsmHandlersSegment` | 0xF000 | Segment for interrupt handler ASM code |

### Timing & Clocks
| Option | Default | Purpose |
|--------|---------|---------|
| `--TimeMultiplier` | 1.0 | Speed up/slow down execution |
| `--InstructionTimeScale` | (none) | PIT timer scale (alternative to TimeMultiplier) |
| `--ClockJitterSeed` | (none) | Deterministic clock jitter offset (±0.01ms) |
| `--ClockStartTime` | UtcNow | Fixed emulated start date/time (ISO 8601) |

### Audio & Sound
| Option | Default | Purpose |
|--------|---------|---------|
| `--AudioEngine` | CrossPlatform | Audio backend selection |
| `--OplMode` | Opl3 | OPL synthesis mode (Opl2/Opl3) |
| `--SbType` | SBPro2 | Sound Blaster variant |
| `--SbBase` | 0x220 | SB I/O port address |
| `--SbIrq` | 7 | SB IRQ line |
| `--SbDma` | 1 | SB 8-bit DMA channel |
| `--SbHdma` | 5 | SB 16-bit HDMA channel |
| `--SbMixer` | true | SB mixer controls OPL voices |
| `-m, --Mt32RomsPath` | (none) | MT-32 ROM zip/directory path |

### Input & Devices
| Option | Default | Purpose |
|--------|---------|---------|
| `--Mouse` | Ps2 | Mouse type: PS/2, serial, or disabled |
| `--StructureFile` | (none) | C header for structure info parsing |

### Debugging & Logging
| Option | Default | Purpose |
|--------|---------|---------|
| `--Debug` | false | Pause at startup for breakpoints |
| `-g, --GdbPort` | 10000 | GDB remote debug server port |
| `--HttpApiPort` | 20000 | HTTP API server port |
| `--McpHttpPort` | 8081 | MCP HTTP server port |
| `-l, --VerboseLogs` | false | Enable verbose log level |
| `-w, --WarningLogs` | false | Enable warning log level |
| `-s, --SilencedLogs` | false | Silence all logs |
| `-f, --FailOnUnhandledPort` | false | Crash on unimplemented I/O ports |

### Override System
| Option | Default | Purpose |
|--------|---------|---------|
| `-o, --OverrideSupplierClassName` | (none) | Class name for C# code overrides |
| `-u, --UseCodeOverride` | true | Whether overrides replace ASM or just names |

**Key property**: `CodeOverridesActive` = UseCodeOverrideOption && OverrideSupplier != null. When active, CFG/execution dumps are not representative of original asm flow.

### DOS & Program
| Option | Default | Purpose |
|--------|---------|---------|
| `-e, --Exe` | (required) | Path to executable |
| `-c, --CDrive` | exe parent | C: drive root directory |
| `-a, --ExeArgs` | (none) | Arguments passed to emulated program |
| `-v, --InitializeDOS` | auto | Install DOS interrupt vectors |
| `-x, --ExpectedChecksum` | (none) | SHA256 hex string for integrity check |

### Data & CFG
| Option | Default | Purpose |
|--------|---------|---------|
| `-r, --RecordedDataDirectory` | program SHA subdir | Dump directory for memory/CFG dumps |
| `--ReloadCfgGraph` | true | Reload previously explored CFG on startup |

## Decision Flow: Which Options to Set

```
Need to debug?
  ├─ Yes → --Debug + --GdbPort <port> (pauses at startup)
  ├─ Need GDB client? → Use seergdb or monitor gdb command
  └─ Parallel runs? → --HttpApiPort 0 --McpHttpPort 0 --GdbPort 0

Need to reverse engineer?
  ├─ Yes → -o MyOverrides + -u true (if source available)
  ├─ Need CFG dump? → --RecordedDataDirectory <path>
  └─ Restart with explored graph? → --ReloadCfgGraph true

Need performance tuning?
  ├─ Faster? → --TimeMultiplier >1.0 or --Cycles higher
  ├─ Deterministic? → --ClockJitterSeed <fixed> + --RenderingMode Sync
  └─ Profile? → --CpuHeavyLog + --CpuHeavyLogExpressions "reg=AX"

Need hardware compatibility?
  ├─ Memory wrap issues? → --A20Gate true
  ├─ EMS/XMS needed? → --Ems true --Xms true (default)
  └─ Wrong sound card? → --SbType SB16 --SbBase 0x330
```

## Common Patterns

### Debugging a specific interrupt
```bash
dotnet run --project src/Spice86 -- -e game.exe --Debug --GdbPort 10000
# Then connect GDB client and set breakpoint at INT vector address
```

### Measuring behavioral diff between two builds
```bash
# Build A
dotnet build src/Spice86.sln -c Release
dotnet run --project src/Spice86 -- -e game.exe -r tmp/buildA

# Build B  
dotnet build src/Spice86.sln -c Release
dotnet run --project src/Spice86 -- -e game.exe -r tmp/buildB

# Compare dumps
diff tmp/buildA/spice86dumpMemoryDump.bin tmp/buildB/spice86dumpMemoryDump.bin
```

### Testing with code overrides
```bash
dotnet run --project src/Spice86 -- -e program.exe -o MyOverrides -u true --CpuHeavyLog true
```

## Verification Checklist

When adding or modifying a Configuration option:
1. [ ] Add `[CommandOption]` attribute with both long and short forms where applicable
2. [ ] Add `[DefaultValue]` if not null/zero/empty
3. [ ] Update this table with the new option
4. [ ] Check `Spice86DependencyInjection.cs` wires the option to a component
5. [ ] Verify no sync-over-async patterns in the wiring path
6. [ ] Run narrow tests: `dotnet test tests/Spice86.Tests --filter-class '*Configuration*' --filter-not-class '*SingleStepTest*'`
