# DOS Mandelbrot Benchmark

This directory contains a CPU-intensive DOS benchmark program that renders Mandelbrot fractals using pure 8086 assembly without FPU support.

## Purpose

This benchmark is designed to test emulator performance improvements, specifically the HasActiveBreakpoints optimization. It provides a real-world workload that:

- Performs intensive integer mathematics
- Uses only 8086 instructions (no FPU)
- Makes minimal DOS/BIOS calls (only for display)
- Runs in an infinite loop, continuously rendering
- Displays performance metrics (frame count)

## Files

- `mandelbrot.asm` - Source code in NASM-compatible assembly
- `mandelbrot.com` - Assembled DOS COM executable (if present)

## Building

To assemble the program, you need NASM (Netwide Assembler):

```bash
nasm -f bin -o mandelbrot.com mandelbrot.asm
```

Or using YASM:

```bash
yasm -f bin -o mandelbrot.com mandelbrot.asm
```

## Running

### In Spice86

```bash
cd path/to/Spice86/bin
./Spice86 -e path/to/mandelbrot.com
```

Or with specific options:

```bash
./Spice86 -e path/to/mandelbrot.com --TimeMultiplier 1
```

### Performance Testing

To measure the performance impact of the HasActiveBreakpoints optimization:

1. **With optimization (this PR)**:
   ```bash
   ./Spice86 -e mandelbrot.com
   # Let it run for 30 seconds, note the frame count
   ```

2. **Without optimization** (compare with master branch or measure cycles):
   - The frame counter displayed shows work completed
   - Higher frame count = better performance

## How It Works

The benchmark:

1. Sets 80x25 text mode using INT 10h
2. Calculates Mandelbrot set using fixed-point arithmetic (8.8 format)
3. Maps iteration counts to ASCII characters (@, #, %, +, =, -, :, ., space)
4. Displays the fractal using INT 10h BIOS calls
5. Shows frame counter at top of screen
6. Loops indefinitely until a key is pressed

### Performance Characteristics

- **CPU Intensive**: Nested loops with multiply and divide operations
- **Minimal I/O**: Only screen updates via INT 10h
- **No FPU**: Uses integer math only
- **Deterministic**: Same calculations every frame
- **No breakpoints**: By default, runs without any breakpoints active

This makes it ideal for measuring the overhead of breakpoint checking in the emulation loop.

## Expected Output

```
Mandelbrot Benchmark - Frames: 0x00000042
Press any key to exit
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
@@@@@@@@@@@@@@@@@@@@@@@@@@@####%%%%++++====--------::::........      
@@@@@@@@@@@@@@@@@@@@@@@###%%%%%+++++====-------:::::........           
@@@@@@@@@@@@@@@@@@@@###%%%%+++++=====------:::::........                
@@@@@@@@@@@@@@@@@###%%%+++++====------:::::........                     
@@@@@@@@@@@@@@###%%%+++====-------::::........                          
[...continues with fractal pattern...]
```

The frame counter increments with each complete render, providing a direct performance metric.

## Technical Details

### Fixed-Point Arithmetic

The program uses 8.8 fixed-point format:
- 8 bits for integer part
- 8 bits for fractional part
- Multiplication results are shifted right to maintain scale
- No floating-point operations

### Coordinate Mapping

- Screen: 80 columns × 24 rows (minus header/footer)
- Complex plane: approximately -2.0 to 1.0 (real), -1.5 to 1.5 (imaginary)
- Center point adjusted for interesting fractal region

### Iteration Count

- Maximum: 16 iterations per pixel
- Escape radius: 2.0 (squared = 4.0)
- Maps to 8 different ASCII characters for visual depth

## Notes

- This is a pure DOS program with no dependencies on Spice86 internals
- Uses only standard DOS INT 21h and BIOS INT 10h interfaces
- Compatible with real DOS systems, DOSBox, and other emulators
- Press any key to exit the benchmark
