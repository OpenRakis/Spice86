# Register View

The Register View component displays CPU register values in the Modern Disassembly View. It provides a clear visualization of register values and highlights changes at the byte level.

## Features

- Displays general registers (EAX, EBX, ECX, EDX, ESI, EDI, EBP, ESP)
- Displays segment registers (CS, DS, ES, FS, GS, SS)
- Displays pointer registers (IP, SP, BP, SI, DI)
- Highlights changes at the byte level:
  - Low byte (AL, BL, etc.)
  - High byte (AH, BH, etc.)
  - Upper word (for 32-bit registers)
- Shows register values in hexadecimal format
- Only displays the 32-bit variant of registers (e.g., EAX) without redundant 16-bit variants (e.g., AX)

## Implementation Details

### RegisterViewModel

The `RegisterViewModel` class tracks individual CPU register values and provides change detection at the byte level:

- `Value` - The current value of the register
- `LowByteChanged` - Indicates if the low byte (bits 0-7) has changed
- `HighByteChanged` - Indicates if the high byte (bits 8-15) has changed
- `UpperWordChanged` - Indicates if the upper word (bits 16-31) has changed

### RegistersViewModel

The `RegistersViewModel` class manages collections of registers and flags, allowing for batch updates and change detection across all registers.

### UI Implementation

The register view is implemented in `ModernDisassemblyView.axaml` using a combination of:

- Styled borders for visual grouping
- Text blocks with conditional visibility based on register type
- Highlighting converters to visually indicate changes
- StringStartsWithConverter to determine register type (e.g., "E" prefix for 32-bit registers)

## Usage

The register view is automatically displayed when the debugger is paused. It updates whenever:

1. The user steps through code (Step Into, Step Over, Step Out)
2. The user pauses execution at a breakpoint
3. The CPU state changes during debugging

Register values that have changed since the last update are highlighted, making it easy to track the effects of instructions on the CPU state.
