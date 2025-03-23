# Modern Disassembly View

The Modern Disassembly View is a component of the Spice86 debugger that provides an enhanced interface for viewing disassembled code.

## Overview

The Modern Disassembly View is designed to provide a clear and efficient way to view and navigate disassembled code.

## Key Requirements

- **Instruction Highlighting**: Must highlight the current instruction based on CPU state
- **Function Navigation**: Must allow navigation to specific functions in the disassembled code
- **Lazy Loading**: Must only be instantiated when the Modern View tab is selected
- **Centered Scrolling**: Must keep the current instruction within the middle 50% of the visible area
- **Thread-Safe UI Updates**: Must ensure all UI operations are performed on the UI thread
- **Efficient Batch Updates**: Must support batch updates of debugger lines to minimize UI update overhead
- **Fast Lookups**: Must provide O(1) lookups for debugger lines by address
- **Syntax Highlighting**: Must provide syntax highlighting for disassembly with different colors for different elements

## Functional Requirements

### Instruction Highlighting

The view must highlight the current instruction being executed:

1. The current instruction must be visually distinct from other instructions
2. The highlighting must update when the CPU state changes
3. The highlighting must be accurate and reflect the actual instruction being executed
4. The highlighting must update when stepping through code or when breakpoints are hit

### Scrolling Behavior

The view must implement an enhanced scrolling behavior:

1. The current instruction must always be visible when debugging
2. The current instruction should be positioned in the middle portion of the screen when possible
3. Scrolling must be smooth and not disorienting to the user
4. The view must automatically scroll to the current instruction when the CPU pauses

### Compact Layout

The view must implement a compact layout to maximize screen real estate:

1. Line height must be minimized without sacrificing readability
2. UI elements must be properly sized and positioned to avoid wasted space
3. The layout must maintain proper alignment of all disassembly elements
4. The compact layout must not affect the functionality of the view

### Property Model

The view model must maintain a clear state model:

1. It must track the current instruction address
2. It must update this address only when appropriate (when the emulator pauses)
3. It must use this address for highlighting and scrolling

### Thread Safety

The implementation must ensure thread safety:

1. All UI updates must be performed on the UI thread
2. Updates from breakpoint callbacks must be marshaled to the UI thread
3. The view must handle updates from different threads gracefully

### Theme-Aware Colors

The disassembly view must provide appropriate colors for both light and dark themes:

1. Text and background colors must have sufficient contrast in both light and dark modes
2. Highlighting colors must be visually distinct but not distracting in both themes
3. Syntax elements (addresses, opcodes, operands) must be visually distinguishable in both themes
4. Boolean converters must transform values to appropriate theme-aware brushes

### Stepping Through Code

The stepping functionality must:

1. Allow stepping into the next instruction
2. Allow stepping over the current instruction when appropriate
3. Update the UI to reflect the new state after stepping
4. Maintain proper highlighting of the current instruction

### Syntax Highlighting

The disassembly view must provide syntax highlighting:

1. Different elements of the disassembly must be displayed in different colors
2. Mnemonics, registers, numbers, and other elements must be visually distinct
3. The highlighting must be consistent across the entire disassembly
4. The implementation must be thread-safe and not create UI elements on non-UI threads
5. The highlighting must use a model that separates formatting from UI creation

### View-ViewModel Decoupling

The architecture must follow proper decoupling:

1. The view must depend only on the interface, not the concrete implementation
2. The interface must define a clear contract between view and view model
3. The implementation must be replaceable without changing the view

### Data Management

The view model must efficiently manage debugger line data:

1. It must provide a dictionary-based storage for O(1) lookups by address
2. It must provide a sorted view for UI display
3. It must support batch updates to minimize UI update overhead
4. It must avoid triggering unnecessary collection change notifications

### Breakpoint Management

The Modern Disassembly View provides comprehensive breakpoint management functionality:

1. **Managing Breakpoints**:
   - Left-clicking the breakpoint indicator toggles the breakpoint between enabled and disabled states
   - Right-clicking a line shows a context menu with options to create or delete breakpoints
   - Context menu items are dynamically enabled/disabled based on the current state of the line:
     - "Create breakpoint here" is only enabled when the line has no breakpoint
     - "Remove breakpoint", "Disable breakpoint", and "Enable breakpoint" are only enabled when the line has a breakpoint
   - The view updates breakpoint indicators immediately when breakpoints are added, removed, or toggled

2. **Breakpoint Visualization**:
   - Breakpoint indicators are visually distinct and easily recognizable
   - The indicators clearly communicate the breakpoint state (enabled/disabled) with appropriate colors
   - The indicators use high-contrast colors (bright red for enabled, grey for disabled) to ensure visibility
   - The indicators are accessible and visible in both light and dark themes
   - The indicators are properly refreshed when breakpoint state changes

3. **Breakpoint Interaction**:
   - The view properly updates when breakpoints are modified from other parts of the UI
   - The view ensures all breakpoint operations are thread-safe
   - The view provides appropriate feedback when breakpoint operations succeed or fail
   - The view provides keyboard shortcuts for common breakpoint operations (e.g., F2 to toggle breakpoint)
   - The view ensures breakpoint state is correctly maintained when reloading disassembly

## Performance Requirements

1. The view must respond quickly to user interactions
2. The view must only update elements that have changed
3. The view must load quickly when the tab is selected
4. The view must handle large disassembly listings efficiently
5. The view must minimize collection change notifications during bulk updates
6. The view must provide fast lookups for addresses when scrolling

## Testing Requirements

The Modern Disassembly View must be tested thoroughly:

1. Unit tests must verify the core functionality
2. Tests must cover edge cases and error conditions
3. Performance tests must ensure the view remains responsive
4. Integration tests must verify correct interaction with other components

## Constraints

1. Must work within the Avalonia UI framework
2. Must be compatible with the existing emulator architecture
3. Must maintain separation of concerns between UI and emulation logic
4. Must be maintainable and extensible for future enhancements
