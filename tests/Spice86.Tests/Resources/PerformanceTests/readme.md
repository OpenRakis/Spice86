# Performance Test Binaries

This directory contains DOS binaries used for automated performance regression testing.

## Files

### mandelbrot_bench.com (577 bytes)
Automated performance benchmark that runs headless for 30 seconds and outputs metrics via I/O port 0x99.

**Usage in tests:**
- Runs automatically as part of the test suite via `PerformanceRegressionTests.MandelbrotBenchmark_ShouldNotRegress()`
- Captures performance data through I/O ports
- Compares against baseline stored in `Resources/PerformanceBaselines/Mandelbrot.json`
- Fails test if performance degrades more than 10%

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

## Manual Testing Binaries

For interactive visual testing, use the VGA versions located in `src/Spice86.MicroBenchmarkTemplate/Resources/`:

### mandelbrot.com (504 bytes)
- Text mode (80x25)
- Frame counter display
- Runs until key press
- Good for quick testing

### mandelbrot_vga.com (577 bytes)  
- VGA Mode 13h (320x200, 256 colors)
- Color gradient palette
- Real-time FPS counter
- Progressive refinement
- Full visual Mandelbrot rendering

**Usage:**
```bash
# Text mode version
./Spice86 -e src/Spice86.MicroBenchmarkTemplate/Resources/mandelbrot.com

# VGA graphics version (recommended)
./Spice86 -e src/Spice86.MicroBenchmarkTemplate/Resources/mandelbrot_vga.com
```

## Source Files

The source assembly files are located in `src/Spice86.MicroBenchmarkTemplate/Resources/`:
- `mandelbrot.asm` - Text mode version
- `mandelbrot_vga.asm` - VGA graphics version  
- `mandelbrot_bench.asm` - Automated test version (headless, port output)

**Building:**
```bash
cd src/Spice86.MicroBenchmarkTemplate/Resources
nasm -f bin mandelbrot_bench.asm -o mandelbrot_bench.com
```

## Integration with CI/CD

The automated test:
1. Runs `mandelbrot_bench.com` in headless mode
2. Captures performance data via I/O port monitoring
3. Compares current run against committed baseline
4. Creates baseline on first run if none exists
5. Fails build if performance degrades >10%

This ensures the HasActiveBreakpoints optimization and other performance improvements don't regress over time.
