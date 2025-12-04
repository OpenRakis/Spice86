# DOS Mandelbrot Benchmark

This directory previously contained CPU-intensive DOS benchmark programs. The Mandelbrot benchmarks have been moved to:

**`tests/Spice86.Tests/Resources/PerformanceTests/`**

This consolidation keeps all performance testing binaries and source code together with the ASM integration tests.

## Available Benchmarks

All files are now in `tests/Spice86.Tests/Resources/PerformanceTests/`:

### VGA Graphics Version (Interactive Manual Testing)
- `mandelbrot_vga.asm` - Source code for 320x200 256-color VGA version
- `mandelbrot_vga.com` - Assembled DOS COM executable (577 bytes)

### Automated Performance Benchmark Version (ASM Integration Test)
- `mandelbrot_bench.asm` - Source code for automated performance testing
- `mandelbrot_bench.com` - Assembled DOS COM executable (577 bytes)
- Runs for fixed duration (30 seconds)
- Outputs FPS data to I/O port 0x99 for headless testing
- Used by `PerformanceRegressionTests` for automated regression testing

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

### VGA Graphics Version (Recommended for Interactive Manual Testing)

```bash
./Spice86 -e tests/Spice86.Tests/Resources/PerformanceTests/mandelbrot_vga.com
```

- Full-color 320x200 VGA graphics
- Real-time FPS counter
- Progressive refinement (increases detail each frame)
- Smooth color gradient palette (256 colors)
- Runs until keypress

### Automated Performance Benchmark (ASM Integration Test)

```bash
./Spice86 -e tests/Spice86.Tests/Resources/PerformanceTests/mandelbrot_bench.com
```

- Headless operation (can run without display)
- Fixed 30-second test duration
- Outputs FPS data to I/O port 0x99
- Generates performance profile for regression detection
- Used by `PerformanceRegressionTests` in test suite

### Performance Testing

**Interactive Testing:**
```bash
./Spice86 -e tests/Spice86.Tests/Resources/PerformanceTests/mandelbrot_vga.com
# Let it run for 30 seconds and observe FPS counter
```

**Automated Regression Testing:**
```bash
dotnet test --filter MandelbrotBenchmark_ShouldMeetPerformanceBaseline
```

The automated test:
- Runs `mandelbrot_bench.com` for 30 seconds headless
- Captures FPS data via I/O port 0x99
- Compares against baseline performance profile
- Fails if performance degrades more than 8%
- Generates baseline in `Resources/PerformanceBaselines/Mandelbrot.json` on first run
- Commits performance profile to track performance over time

## How It Works

### VGA Graphics Version

1. Sets VGA Mode 13h (320x200 256-color) using INT 10h
2. Configures 256-color palette with smooth gradient (black→blue→cyan→yellow→white)
3. Calculates Mandelbrot set using fixed-point arithmetic (8.8 format)
4. Progressive refinement: starts at 64 iterations, increases to 255
5. Slow zoom animation toward interesting fractal region
6. Measures and displays FPS using BIOS timer (INT 1Ah)
7. Direct VGA memory writes (0xA000 segment) for pixel plotting
8. Loops indefinitely until a key is pressed

### Performance Characteristics

- **CPU Intensive**: Nested loops with multiply and divide operations
- **Minimal I/O**: Direct VGA memory writes, minimal BIOS calls
- **No FPU**: Uses integer math only
- **Progressive**: Increasing detail shows sustained performance
- **No breakpoints**: By default, runs without any breakpoints active
- **Real metrics**: FPS counter provides quantifiable performance data

This makes it ideal for measuring the overhead of breakpoint checking in the emulation loop.

## Expected Output

### VGA Graphics Version

```
FPS: 24
Iterations: 128

[Full-screen 320x200 colorful Mandelbrot fractal with smooth gradient]
- Black regions (set members)
- Blue→Cyan→Yellow→White gradient (escape iterations)
- Progressive detail increase visible each frame
- Slow zoom animation toward fractal details
```

The FPS counter provides real-time performance feedback, updating every second.

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
