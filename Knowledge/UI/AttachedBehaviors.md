# Attached Behaviors

## Overview

Attached behaviors are a pattern used in the Spice86 UI to separate UI-specific logic from the view code-behind. This improves the MVVM architecture by:

1. Keeping UI-specific logic in dedicated behavior classes
2. Reducing code in the view code-behind
3. Making UI logic more reusable
4. Improving testability

## Available Behaviors

### DisassemblyScrollBehavior

The `DisassemblyScrollBehavior` handles scrolling logic for the disassembly view. It automatically scrolls the disassembly list to center the current instruction when the `CurrentInstructionAddress` property changes.

#### Usage

```xml
<ListBox
    ItemsSource="{Binding DebuggerLines.Values}"
    behaviors:DisassemblyScrollBehavior.IsEnabled="True"
    behaviors:DisassemblyScrollBehavior.TargetAddress="{Binding CurrentInstructionAddress}">
    <!-- Item template -->
</ListBox>
```

#### Properties

- `IsEnabled` - Enables or disables the behavior
- `TargetAddress` - The address to scroll to

### InstructionPointerBehavior

The `InstructionPointerBehavior` handles pointer events for instruction items in the disassembly view. It automatically selects the clicked instruction and updates the `SelectedDebuggerLine` property in the view model.

#### Usage

```xml
<ContentControl
    behaviors:InstructionPointerBehavior.IsEnabled="True">
    <!-- Content -->
</ContentControl>
```
