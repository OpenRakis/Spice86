# Scrolling Strategy for Modern Disassembly View

## Core Principles
- Context Preservation: Always maintain visual context around the current instruction
- Predictable Navigation: Ensure consistent behavior for all navigation methods
- Minimal Disruption: Avoid jarring scroll position changes
- MVVM Compliance: Use attached behaviors for UI-specific logic

## Detailed Scrolling Strategy
### Current Instruction Centering
When the CPU pauses (breakpoint hit, step operation, or manual pause):

- The current instruction (at CPU's IP) should be positioned in the middle 50% of the visible area
- This provides optimal context with roughly equal space above and below
- The instruction should be visually highlighted with a distinct background color
- If the current instruction is already in the middle zone, no scrolling occurs to minimize disruption

### Keyboard Navigation Behavior

#### Arrow Keys (Up/Down)
- Up Arrow: Move selection up one instruction, maintaining the selected instruction's relative position in the viewport
- Down Arrow: Move selection down one instruction, maintaining the selected instruction's relative position in the viewport
- When selection approaches viewport edges (within 20% of top/bottom), scroll to keep selection within the middle zone
- Selection should be visually distinct from the current instruction highlight

#### Page Up/Down Keys
- Page Up: Move selection up by the number of visible instructions minus 2 (for context), scrolling the view accordingly
- Page Down: Move selection down by the number of visible instructions minus 2 (for context), scrolling the view accordingly
- After paging, position the selection in the middle of the viewport
- This provides some overlap between "pages" for better context

#### Home/End Keys
- Home: Jump to the first instruction in the current disassembly range, positioning it at the top 25% of the viewport
- End: Jump to the last instruction in the current disassembly range, positioning it at the bottom 25% of the viewport

### CPU Execution Behavior
When the CPU runs and then stops again:

- Immediately update the current instruction highlight to match the new IP address
- If the new current instruction is already visible in the middle zone of the viewport:
  - Do not change the scroll position (minimal disruption)
- If the new current instruction is visible but not in the middle zone:
  - Smoothly scroll to position it in the middle of the viewport
- If the new current instruction is not visible:
  - Load the appropriate disassembly range if needed
  - Scroll to position the instruction in the middle of the viewport

### "Go to Current Instruction" Button Behavior
When the user clicks the "Go to Current Instruction" button:

- Always scroll to center the current instruction in the viewport, regardless of its current visibility
- This provides a consistent way for users to re-center the view on the current execution point

### Mouse Interaction
- Clicking an instruction should select it without changing the scroll position
- Double-clicking an instruction should set a breakpoint
- Right-clicking should show a context menu with options like "Run to here", "Set breakpoint", etc.
- Mouse wheel should scroll normally, respecting the user's system settings for scroll speed

## Implementation Details

### MVVM Architecture with Attached Behaviors

The scrolling strategy is implemented using Avalonia's attached behaviors to maintain proper separation of concerns:

#### DisassemblyScrollBehavior
- Handles all scrolling logic for the disassembly view
- Attached to the ListBox in XAML via `behaviors:DisassemblyScrollBehavior.IsEnabled="True"`
- Monitors changes to the `CurrentInstructionAddress` property via `behaviors:DisassemblyScrollBehavior.TargetAddress="{Binding CurrentInstructionAddress}"`
- Automatically scrolls to center the instruction when the address changes
- Implements middle zone detection to avoid unnecessary scrolling
- Handles all the complex UI calculations for proper centering

#### InstructionPointerBehavior
- Handles pointer events for instruction items
- Attached to instruction ContentControl via `behaviors:InstructionPointerBehavior.IsEnabled="True"`
- Updates the SelectedDebuggerLine property in the ViewModel when an instruction is clicked
- Handles context menu interactions through data binding

### Key Properties to Track
- CurrentInstructionAddress: The physical address of the current instruction (CPU's IP)
- SelectedDebuggerLine: The user-selected instruction line (may differ from current)

### Middle Zone Detection:
- Define the middle zone as the middle part of the viewport that excludes the top and bottom 3 lines
- Check if an item is fully contained within this zone
- Avoid unnecessary scrolling if the item is already in the middle zone

### Key Methods in DisassemblyScrollBehavior
- ScrollToAddress(listBox, targetAddress): Scrolls to position an item in the middle of viewport
- CenterContainerInViewport(scrollViewer, container, targetAddress, startTime): Centers a container in the viewport
- GetVisibleDebuggerLines(listBox): Gets the currently visible instruction lines
- GetMiddleItems(items): Gets the items in the middle zone of the viewport

### Event-Based Scrolling
- Uses the LayoutUpdated event to detect when containers are materialized
- Automatically centers the current instruction when it becomes visible
- Unsubscribes from events when not needed to improve performance
- Avoids retry mechanisms in favor of event-driven approach

### Thread Safety
- All UI updates are performed on the UI thread using Dispatcher.UIThread.Post()
- Asynchronous operations are properly handled to avoid UI freezing

