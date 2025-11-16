# Docking Framework Implementation

## Overview

Spice86's debugger window now uses the Avalonia.UpDock docking framework to provide a Visual Studio-like docking experience. This allows users to detach debugger tabs into separate windows and re-dock them as needed.

## Features

- **Detachable Tabs**: First-level debugger tabs can be dragged out to create floating windows
- **Re-docking**: Floating windows can be dragged back and docked in various positions
- **Non-Closable Tabs**: Tabs cannot be accidentally closed (using standard `TabItem` instead of `ClosableTabItem`)
- **Nested Tab Preservation**: Second-level tabs (e.g., multiple disassembly views, device tabs) remain as standard tabs
- **Hotkey Support**: All existing hotkeys (Alt+F1 through Alt+F10) are preserved

## Architecture

### Components

1. **DockSpacePanel**: Root container for the docking system
   - Manages the overall docking tree structure
   - Handles drag-and-drop operations between docked and floating windows

2. **RearrangeTabControl**: Container for dockable tabs
   - Enables drag-and-drop of tabs
   - Supports tab rearrangement within the control
   - Creates floating windows when tabs are dragged out

3. **TabItem**: Standard Avalonia TabItem used for first-level tabs
   - Non-closable (no close button)
   - Draggable to create floating windows
   - Supports all standard tab features (hotkeys, visibility, etc.)

### First-Level Dockable Tabs

The following tabs are dockable and detachable:

1. **Disassembly** (Alt+F1)
   - Contains nested TabControl with multiple disassembly views
   
2. **Code Flow** (Alt+F2)
   - Single view of CFG CPU analysis
   - Visibility controlled by CfgCpu enable flag
   
3. **CPU** (Alt+F3)
   - Single view of CPU state and registers
   
4. **Memory** (Alt+F4)
   - Contains nested TabControl with multiple memory views
   
5. **Devices** (Alt+F5)
   - Contains nested TabControl with device-specific tabs:
     - Video Card (Alt+F7)
     - Color Palette (Alt+F8)
     - General MIDI / MT-32 (Alt+F9)
     - Software Mixer (Alt+F10)
   
6. **Breakpoints** (Alt+F6)
   - Single view for managing breakpoints

## Implementation Details

### Avalonia.UpDock Library

The Avalonia.UpDock library was integrated as a project reference:

- **Location**: `src/Avalonia.UpDock/`
- **Target Framework**: Modified from net9.0 to net8.0 to match Spice86
- **Dependencies**: Requires System.Reactive package (added to Directory.Packages.props)

### XAML Structure

The DebugWindow.axaml was updated from:

```xml
<TabControl TabStripPlacement="Left" Grid.Row="1">
    <controls:HotKeyTabItem ...>
        <!-- Tab content -->
    </controls:HotKeyTabItem>
    <!-- More tabs -->
</TabControl>
```

To:

```xml
<up:DockSpacePanel Grid.Row="1">
    <up:RearrangeTabControl>
        <TabItem HotKeyManager.HotKey="Alt+F1" Header="Disassembly">
            <!-- Tab content -->
        </TabItem>
        <!-- More tabs -->
    </up:RearrangeTabControl>
</up:DockSpacePanel>
```

### Key Changes

1. Replaced outer `TabControl` with `DockSpacePanel`
2. Replaced `HotKeyTabItem` with standard `TabItem` for first-level tabs
3. Removed rotated tab headers (tabs now appear horizontally at the top)
4. Preserved all nested TabControls within their parent tabs
5. Maintained all hotkey bindings and data bindings

## Usage

### Detaching a Tab

1. Click and hold on a tab header
2. Drag the tab away from the tab control
3. Release to create a floating window

### Re-docking a Tab

1. Click and hold on a floating window's tab header
2. Drag it over the main window
3. Drop zones will appear showing where the tab can be docked
4. Release over a drop zone to dock the tab

### Keyboard Navigation

All original keyboard shortcuts are preserved:

- Alt+F1: Disassembly
- Alt+F2: Code Flow
- Alt+F3: CPU
- Alt+F4: Memory
- Alt+F5: Devices
- Alt+F6: Breakpoints
- Alt+F7: Video Card (within Devices)
- Alt+F8: Color Palette (within Devices)
- Alt+F9: General MIDI / MT-32 (within Devices)
- Alt+F10: Software Mixer (within Devices)

## Testing

All existing unit tests continue to pass:
- 693 tests passed
- 1 test skipped
- 0 tests failed

## Future Enhancements

Potential improvements for the future:

1. **Persistence**: Save and restore dock layout between sessions
2. **Customizable Layouts**: Allow users to save multiple layout presets
3. **Closable Tabs**: Optionally allow tabs to be closed with a way to reopen them
4. **More Flexible Layouts**: Support for more complex docking arrangements (split panels, etc.)

## References

- [Avalonia.UpDock GitHub Repository](https://github.com/jupahe64/Avalonia.UpDock)
- [Spice86 Internal Debugger Wiki](https://github.com/OpenRakis/Spice86/wiki/Spice86-internal-debugger)
