# Performance Test Binaries and Source Code

This directory contains DOS binaries and source code used for automated performance regression testing and manual interactive testing.

## Files

### Automated Performance Benchmark (ASM Integration Test)

**mandelbrot_bench.com (577 bytes)** and **mandelbrot_bench.asm**

Automated performance benchmark that runs headless for 30 seconds and outputs metrics via I/O port 0x99.

**Usage in tests:**
- Runs automatically as part of the test suite via `PerformanceRegressionTests.MandelbrotBenchmark_ShouldNotRegress()`
- Captures performance data through I/O ports
- Compares against baseline stored in `Resources/PerformanceBaselines/Mandelbrot.json`
- Fails test if performance degrades more than 8%

**I/O Port Protocol (port 0x99):**
- `0xFF` - Start marker (sent at beginning)
- `[low, high]` - FPS values during execution (16-bit, sent periodically)
- `0xFE` - End marker (sent at completion)
- `[low, high]` - Total frames rendered (16-bit)
- `[value]` - Average FPS (8-bit)

**Manual testing:**
```bash
./Spice86 -e tests/Spice86.Tests/Resources/PerformanceTests/mandelbrot_bench.com
```

### Interactive Manual Testing Binary

**mandelbrot_vga.com (577 bytes)** and **mandelbrot_vga.asm**

VGA graphics version for visual performance validation:
- VGA Mode 13h (320x200, 256 colors)
- Color gradient palette (black→blue→cyan→yellow→white)
- Real-time FPS counter displayed on screen
- Progressive refinement (32→128 iterations)
- Full visual Mandelbrot fractal rendering
- Runs until key press

**Usage:**
```bash
# VGA graphics version (recommended for manual testing)
./Spice86 -e tests/Spice86.Tests/Resources/PerformanceTests/mandelbrot_vga.com
```

## Building from Source

All source assembly files (.asm) are in this directory alongside their compiled binaries (.com).

**Building with NASM:**
```bash
cd tests/Spice86.Tests/Resources/PerformanceTests
nasm -f bin mandelbrot_bench.asm -o mandelbrot_bench.com
nasm -f bin mandelbrot_vga.asm -o mandelbrot_vga.com
```

**Building with YASM:**
```bash
cd tests/Spice86.Tests/Resources/PerformanceTests
yasm -f bin mandelbrot_bench.asm -o mandelbrot_bench.com
yasm -f bin mandelbrot_vga.asm -o mandelbrot_vga.com
```

## Integration with CI/CD

The automated test:
1. Runs `mandelbrot_bench.com` in headless mode for 30 seconds
2. Captures performance data via I/O port 0x99 monitoring
3. Compares current run against committed baseline
4. Creates baseline on first run if none exists
5. Fails build if performance degrades >8%

This ensures the HasActiveBreakpoints optimization and other performance improvements don't regress over time.

## Technical Details

### Fixed-Point Arithmetic
- Uses 8.8 fixed-point format (8 bits integer, 8 bits fractional)
- Pure integer operations (no FPU)
- 8086-compatible assembly only

### Mandelbrot Algorithm
- Escape-time algorithm with maximum iteration limits
- Coordinate mapping from screen space to complex plane
- Color/ASCII mapping based on iteration count before escape

### Performance Characteristics
- CPU-intensive nested loops
- Minimal BIOS/DOS calls
- Direct VGA memory writes (VGA version)
- Continuous rendering for sustained load
- Real performance metrics (FPS, frame count)
